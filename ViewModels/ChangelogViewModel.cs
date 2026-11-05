using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using upeko.Services;

namespace upeko.ViewModels;

/// <summary>
/// The in-app NadekoBot changelog dialog.
/// </summary>
public partial class ChangelogViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private int _requestId;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVersions))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(ShowVersions))]
    private string? _error;

    [ObservableProperty]
    private IReadOnlyList<ChangelogVersion> _versions = [];

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string? _subtitle;

    /// <summary>
    /// The bot the dialog was opened for, when it has an update; the dialog then offers the update.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    private BotViewModel? _botToUpdate;

    public bool HasError => Error is not null;
    public bool ShowVersions => !IsLoading && !HasError;
    public bool CanUpdate => BotToUpdate is { CanUpdate: true };

    public ChangelogViewModel(MainWindowViewModel main)
    {
        _main = main;
    }

    /// <summary>
    /// Opens the dialog with the changes newer than <paramref name="sinceVersion"/>.
    /// </summary>
    public async Task OpenAsync(string? sinceVersion, BotViewModel? bot)
    {
        var request = ++_requestId;
        BotToUpdate = bot is { CanUpdate: true } ? bot : null;
        Error = null;
        Versions = [];
        Title = Loc["Changelog_Title"];
        Subtitle = null;
        IsLoading = true;
        IsOpen = true;

        try
        {
            var all = await ChangelogService.Instance.GetAsync();
            if (request != _requestId)
                return;

            var shown = ChangelogService.GetSince(all, sinceVersion);
            Versions = shown;

            var installed = sinceVersion?.Trim().TrimStart('v');
            var showsRange = installed is not null
                             && shown.Count > 0
                             && VersionUtil.Compare(shown[^1].Version, installed) > 0;
            Subtitle = showsRange
                ? string.Format(Loc["Changelog_Since"], installed)
                : Loc["Changelog_Latest"];
        }
        catch (Exception ex)
        {
            if (request != _requestId)
                return;
            Error = string.Format(Loc["Changelog_LoadFailed"], ex.Message);
        }
        finally
        {
            if (request == _requestId)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        _requestId++;
        IsOpen = false;
        BotToUpdate = null;
    }

    [RelayCommand]
    private void OpenOnGitHub()
    {
        _main.OpenUrlCommand.Execute(ChangelogService.ChangelogPageUrl);
    }

    [RelayCommand]
    private void Update()
    {
        var bot = BotToUpdate;
        Close();
        if (bot is { CanUpdate: true })
            bot.UpdateBotCommand.Execute(null);
    }
}
