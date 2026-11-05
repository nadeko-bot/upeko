using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using upeko.Models;
using upeko.Services;

namespace upeko.ViewModels;

public partial class BotListViewModel : ViewModelBase
{
    private const string ChangelogUrl = "https://github.com/nadeko-bot/nadekobot/blob/v6/CHANGELOG.md";

    private readonly MainWindowViewModel _main;
    private readonly IBotRepository _botRepository;

    public ObservableCollection<BotViewModel> Bots { get; }

    private static readonly TimeSpan CloudPromoHideTime = TimeSpan.FromDays(30);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloudStrip))]
    private bool _isEmpty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloudStrip))]
    private bool _isCloudStripHidden;

    /// <summary>
    /// The one-line NadeKloud strip under a non-empty list. The empty list shows a bigger card instead.
    /// </summary>
    public bool ShowCloudStrip => !IsEmpty && !IsCloudStripHidden;

    public MainWindowViewModel Main => _main;

    public BotListViewModel(MainWindowViewModel main)
    {
        _main = main;
        _botRepository = new JsonBotRepository();

        if (_botRepository.RecoveredFromBackup)
            _main.ShowToast(Loc["Toast_RecoveredFromBackup"], ToastKind.Info);

        var hiddenUntil = _botRepository.GetConfig().CloudPromoHiddenUntil;
        _isCloudStripHidden = hiddenUntil is not null && hiddenUntil > DateTime.UtcNow;

        Bots = new ObservableCollection<BotViewModel>();
        LoadBots();

        UpdateEmpty();
        Bots.CollectionChanged += (_, _) => UpdateEmpty();

        LocalizationService.Instance.LanguageChanged += () => OnPropertyChanged(nameof(Loc));

        _ = Dispatcher.UIThread.InvokeAsync(ResumeSessionAsync);
    }

    private void LoadBots()
    {
        Bots.Clear();
        foreach (var botModel in _botRepository.GetBots())
            Bots.Add(new BotViewModel(this, botModel));
    }

    private async Task ResumeSessionAsync()
    {
        // Give the bots a moment to read their versions, so IsReady is known.
        await Task.Delay(1000);

        var botsToResume = Bots.Where(b => b.Bot.WasRunning && b.IsReady).ToList();
        if (botsToResume.Count == 0)
            return;

        Log.Information("Resuming {Count} bot(s) from previous session", botsToResume.Count);
        ShowToast(string.Format(Loc["Toast_ResumingBots"], botsToResume.Count));

        foreach (var bot in botsToResume)
        {
            bot.StartBotCommand.Execute(null);

            if (bot != botsToResume[^1])
                await Task.Delay(3000);
        }
    }

    private void UpdateEmpty()
    {
        IsEmpty = Bots.Count == 0;
    }

    [RelayCommand]
    private void SelectBot(BotViewModel bot)
    {
        _main.NavigateToDetail(bot);
    }

    [RelayCommand]
    private void AddBot()
    {
        var guid = Guid.NewGuid();
        var botName = $"bot-{guid.ToString()[..5]}";

        var defaultPath = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "upeko",
            botName));

        var botModel = new BotModel
        {
            Guid = guid,
            Name = botName,
            PathUri = defaultPath
        };

        _botRepository.AddBot(botModel);
        Bots.Add(new BotViewModel(this, botModel));
        Log.Information("Created bot instance {BotName} at {BotPath}", botModel.Name, defaultPath);
        ShowToast(Loc["Toast_InstanceCreated"], ToastKind.Success);
    }

    [RelayCommand]
    private void HideCloudStrip()
    {
        IsCloudStripHidden = true;
        _botRepository.GetConfig().CloudPromoHiddenUntil = DateTime.UtcNow + CloudPromoHideTime;
        _botRepository.SaveConfig();
    }

    [RelayCommand]
    private void ShowChangelog()
    {
        PlatformSpecific.Open(ChangelogUrl);
    }

    public void UpdateBot(BotModel bot)
    {
        _botRepository.UpdateBot(bot);
    }

    public void RemoveBot(BotViewModel botViewModel)
    {
        Log.Information("Deleted bot instance {BotName}", botViewModel.Bot.Name);
        botViewModel.PrepareForRemoval();
        _botRepository.RemoveBot(botViewModel.Bot);
        Bots.Remove(botViewModel);
    }

    /// <summary>
    /// Stops all running bots because upeko is quitting.
    /// </summary>
    public void StopAllForExit()
    {
        foreach (var bot in Bots)
            bot.StopForExit();
    }

    public void RemoveNavigation()
    {
        _main.NavigateToList();
    }

    public void RequestDelete(string botName)
    {
        _main.RequestDelete(botName);
    }

    public void ShowToast(string message, ToastKind kind = ToastKind.Info)
        => _main.ShowToast(message, kind);

    public ConfigModel GetConfig() => _botRepository.GetConfig();

    public void SaveConfig() => _botRepository.SaveConfig();
}
