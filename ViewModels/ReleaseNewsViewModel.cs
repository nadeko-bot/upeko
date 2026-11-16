using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using upeko.Services;

namespace upeko.ViewModels;

public enum ReleaseNewsState
{
    Hidden,
    /// <summary>A bot is older than the release.</summary>
    Available,
    /// <summary>Every bot has the release, but the user did not read its notes.</summary>
    Updated,
}

/// <summary>
/// The strip above the bot list that announces a NadekoBot release whose notes the user has not read.
/// </summary>
public partial class ReleaseNewsViewModel : ViewModelBase
{
    private readonly BotListViewModel _list;
    private bool _versionsRead;

    // What the strip should show; it shows only after the summary of this range is loaded.
    private (ReleaseNewsState State, string Since, string Latest)? _target;
    private string? _loadingKey;
    private string? _summaryKey;
    private ChangelogSummary? _summary;
    private string? _since;
    private string? _latest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(IsAvailable), nameof(IsUpdated))]
    private ReleaseNewsState _state;

    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// The line under the title in parts ("Changes since v7.10.0", "6 changed"), so the view can wrap between parts and never inside one.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<string> _detailParts = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore))]
    private string? _moreText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTopChanges))]
    private IReadOnlyList<ChangelogItem> _topChanges = [];

    public bool IsVisible => State != ReleaseNewsState.Hidden;
    public bool IsAvailable => State == ReleaseNewsState.Available;
    public bool IsUpdated => State == ReleaseNewsState.Updated;
    public bool HasTopChanges => TopChanges.Count > 0;
    public bool HasMore => MoreText is not null;

    public ReleaseNewsViewModel(BotListViewModel list)
    {
        _list = list;
        foreach (var bot in list.Bots)
            bot.PropertyChanged += OnBotChanged;
        list.Bots.CollectionChanged += OnBotsChanged;
        UpdateChecker.Instance.OnNewVersionFound += _ => Dispatcher.UIThread.Post(Refresh);
        LocalizationService.Instance.LanguageChanged += ApplyTexts;

        Refresh();
        _ = WaitForVersionsAsync();
    }

    private async Task WaitForVersionsAsync()
    {
        try
        {
            await Task.WhenAll(_list.Bots.Select(b => b.VersionLoaded));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the version of every bot for the release strip");
        }

        _versionsRead = true;
        Refresh();
    }

    private void OnBotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (BotViewModel bot in e.OldItems)
                bot.PropertyChanged -= OnBotChanged;
        if (e.NewItems is not null)
            foreach (BotViewModel bot in e.NewItems)
                bot.PropertyChanged += OnBotChanged;
        Refresh();
    }

    private void OnBotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BotViewModel.Version) or nameof(BotViewModel.IsBotDownloaded))
            Refresh();
    }

    /// <summary>
    /// Works out what the strip shows from the latest release, the bot versions and the seen release.
    /// </summary>
    public void Refresh()
    {
        var config = _list.GetConfig();
        var live = UpdateChecker.Instance.LatestRelease?.TagName;
        if (!string.IsNullOrWhiteSpace(live) && live != config.LatestBotRelease)
        {
            config.LatestBotRelease = live;
            _list.SaveConfig();
        }

        var tag = config.LatestBotRelease;
        var latest = Clean(tag);
        var installed = _list.Bots
            .Select(b => Clean(b.Version))
            .OfType<string>()
            .ToList();

        if (tag is null || latest is null || installed.Count == 0)
        {
            Hide();
            return;
        }

        if (Clean(config.SeenBotRelease) is null)
        {
            // The first time, the unread notes start at the oldest bot, so a new install shows no strip.
            if (!_versionsRead)
            {
                Hide();
                return;
            }

            var oldest = installed.OrderBy(v => v, Comparer<string>.Create(VersionUtil.Compare)).First();
            config.SeenBotRelease = VersionUtil.Compare(oldest, latest) < 0 ? oldest : latest;
            _list.SaveConfig();
        }

        var seen = Clean(config.SeenBotRelease)!;
        if (VersionUtil.Compare(latest, seen) <= 0)
        {
            Hide();
            return;
        }

        var state = installed.Any(v => VersionUtil.Compare(v, latest) < 0)
            ? ReleaseNewsState.Available
            : ReleaseNewsState.Updated;
        _target = (state, seen, latest);
        _ = ShowAsync(tag);
    }

    private void Hide()
    {
        _target = null;
        State = ReleaseNewsState.Hidden;
    }

    private async Task ShowAsync(string tag)
    {
        if (_target is not { } target)
            return;

        var key = target.Since + "|" + target.Latest;
        if (key == _summaryKey)
        {
            Apply();
            // A failed load is tried again on the next refresh; the strip shows without the changes meanwhile.
            if (_summary is not null)
                return;
        }

        if (key == _loadingKey)
            return;

        _loadingKey = key;
        var summary = await LoadSummaryAsync(target.Since, target.Latest, tag);
        if (_loadingKey != key)
            return;

        _loadingKey = null;
        if (summary is not null || _summaryKey != key)
        {
            _summaryKey = key;
            _summary = summary;
        }

        Apply();
    }

    private static async Task<ChangelogSummary?> LoadSummaryAsync(string since, string latest, string tag)
    {
        try
        {
            var all = ChangelogService.Instance.TryGetCached(tag) ?? await ChangelogService.Instance.GetAsync();
            return ChangelogService.Summarize(ChangelogService.Between(all, since, latest));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load the notes for the release strip");
            return null;
        }
    }

    private void Apply()
    {
        if (_target is not { } target || target.Since + "|" + target.Latest != _summaryKey)
            return;

        _since = target.Since;
        _latest = target.Latest;
        State = target.State;
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        if (_since is null || _latest is null)
            return;

        Title = string.Format(Loc[State == ReleaseNewsState.Updated ? "ReleaseNews_Updated" : "ReleaseNews_Available"], _latest);

        var parts = new List<string> { string.Format(Loc["Changelog_Since"], _since) };
        if (_summary is { } s)
        {
            if (s.Added > 0) parts.Add(string.Format(Loc["ReleaseNews_Added"], s.Added));
            if (s.Changed > 0) parts.Add(string.Format(Loc["ReleaseNews_Changed"], s.Changed));
            if (s.Fixed > 0) parts.Add(string.Format(Loc["ReleaseNews_Fixed"], s.Fixed));
            if (s.Removed > 0) parts.Add(string.Format(Loc["ReleaseNews_Removed"], s.Removed));
        }

        DetailParts = parts;
        TopChanges = _summary?.Top ?? [];
        var more = _summary is { } m ? m.Total - m.Top.Count : 0;
        MoreText = more > 0 ? string.Format(Loc["ReleaseNews_More"], more) : null;
    }

    /// <summary>
    /// Records that the user read the notes up to <paramref name="release"/>. An older release changes nothing.
    /// </summary>
    public void MarkSeen(string release)
    {
        var version = Clean(release);
        if (version is null)
            return;

        var config = _list.GetConfig();
        if (Clean(config.SeenBotRelease) is { } seen && VersionUtil.Compare(version, seen) <= 0)
            return;

        config.SeenBotRelease = version;
        _list.SaveConfig();
        Log.Information("NadekoBot release notes seen up to {Version}", version);
        Refresh();
    }

    [RelayCommand]
    private Task WhatsNew()
    {
        // The dialog offers the update only when it is clear which bot it is for.
        var updatable = _list.Bots.Where(b => b.CanUpdate).ToList();
        return _list.Main.Changelog.OpenAsync(_since, updatable.Count == 1 ? updatable[0] : null);
    }

    [RelayCommand]
    private void Dismiss()
    {
        if (_latest is not null)
            MarkSeen(_latest);
    }

    private static string? Clean(string? version)
        => string.IsNullOrWhiteSpace(version) ? null : version.Trim().TrimStart('v');
}
