using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        Bots = new ObservableCollection<BotViewModel>();
        LoadBots();

        UpdateEmpty();
        Bots.CollectionChanged += (_, _) => UpdateEmpty();
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
        _main.ShowToast("Instance created", "success");
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
}