using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
    private readonly MainWindowViewModel _main;
    private readonly IBotRepository _botRepository;

    public ObservableCollection<BotViewModel> Bots { get; }

    [ObservableProperty]
    private bool _isEmpty;

    public BotListViewModel(MainWindowViewModel main)
    {
        _main = main;
        _botRepository = new JsonBotRepository();

        if (_botRepository.RecoveredFromBackup)
            _main.ShowToast(Loc["Toast_RecoveredFromBackup"], "info");

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
        var botModels = _botRepository.GetBots();
        foreach (var botModel in botModels)
        {
            Bots.Add(new BotViewModel(this, botModel));
        }
    }

    private async Task ResumeSessionAsync()
    {
        await Task.Delay(1000);

        var botsToResume = new List<BotViewModel>();
        foreach (var bot in Bots)
        {
            if (bot.Bot.WasRunning && bot.IsReady)
                botsToResume.Add(bot);
        }

        if (botsToResume.Count == 0)
            return;

        Log.Information("Resuming {Count} bot(s) from previous session", botsToResume.Count);
        _main.ShowToast(string.Format(Loc["Toast_ResumingBots"], botsToResume.Count));

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
        var first5 = guid.ToString()[..5];
        var botName = $"bot-{first5}";

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
        _main.ShowToast(Loc["Toast_InstanceCreated"], "success");
    }

    [RelayCommand]
    private void ShowChangelog()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/nadeko-bot/nadekobot/blob/v6/CHANGELOG.md",
            UseShellExecute = true
        });
    }

    public void UpdateBot(BotModel bot)
    {
        _botRepository.UpdateBot(bot);
    }

    public void RemoveBot(BotViewModel botViewModel)
    {
        Log.Information("Deleted bot instance {BotName}", botViewModel.Bot.Name);
        _botRepository.RemoveBot(botViewModel.Bot);
        Bots.Remove(botViewModel);
    }

    public void RemoveNavigation()
    {
        _main.NavigateToList();
    }

    public void RequestDelete(string botName)
    {
        _main.RequestDelete(botName);
    }

    public ConfigModel GetConfig() => _botRepository.GetConfig();

    public void SaveConfig() => _botRepository.SaveConfig();
}