using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using upeko.Models;
using upeko.Services;

namespace upeko.ViewModels;

public record LanguageOption(string Code, string DisplayName);

public enum ToastKind
{
    Info,
    Success,
    Error,
}

public partial class MainWindowViewModel : ViewModelBase
{
    private const string UpEkoReleaseUrl = "https://github.com/nadeko-bot/upeko/releases/latest";
    private const string DiscordUrl = "https://discord.gg/nadekobot";

    private static readonly System.Net.Http.HttpClient _httpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Upeko-Update-Checker" } }
    };

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private bool _isUpEkoUpdateAvailable;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private ToastKind _toastKind = ToastKind.Info;

    [ObservableProperty]
    private bool _isDeleteModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteModalMessage))]
    private string _deleteModalBotName = string.Empty;

    [ObservableProperty]
    private bool _isExitModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExitModalMessage))]
    private int _runningBotCount;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private LanguageOption _selectedLanguageOption;

    public List<LanguageOption> LanguageOptions { get; } = LocalizationService.Languages
        .Select(kv => new LanguageOption(kv.Key, kv.Value))
        .ToList();

    public FfmpegDepViewModel Ffmpeg { get; }
    public YtdlDepViewModel Ytdlp { get; }
    public JsDepViewModel Js { get; }

    private readonly BotListViewModel _botListViewModel;
    private DispatcherTimer? _toastTimer;

    public string CurrentVersion { get; }

    public string DeleteModalMessage =>
        $"{Loc["Modal_DeletePrefix"]}{DeleteModalBotName}{Loc["Modal_DeleteSuffix"]}";

    public string ExitModalMessage =>
        string.Format(Loc["Modal_ExitMessage"], RunningBotCount);

    /// <summary>
    /// Raised when the ViewModel wants the app to quit.
    /// MainWindow subscribes to this in OnDataContextChanged.
    /// </summary>
    public Action? CloseRequested { get; set; }

    public ConfigModel GetConfig() => _botListViewModel.GetConfig();

    public MainWindowViewModel()
    {
        Ffmpeg = new FfmpegDepViewModel();
        Ytdlp = new YtdlDepViewModel();
        Js = new JsDepViewModel();

        _botListViewModel = new BotListViewModel(this);
        _currentView = _botListViewModel;

        var config = _botListViewModel.GetConfig();
        _minimizeToTray = config.MinimizeToTray;
        _selectedLanguageOption = LanguageOptions.FirstOrDefault(l => l.Code == config.Language)
                                  ?? LanguageOptions.First();

        // Show "1.2.3", not the assembly's "1.2.3.0".
        CurrentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

        _isDarkTheme = config.IsDarkTheme;
        if (Application.Current != null)
            Application.Current.RequestedThemeVariant = _isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(1000);
            await Task.WhenAll(
                CheckDepsAsync(),
                CheckForUpEkoUpdatesAsync());
        });
    }

    private async Task CheckDepsAsync()
    {
        await Ffmpeg.CheckAsync();
        await Ytdlp.CheckAsync();
        await Js.CheckAsync();
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Loc));
        OnPropertyChanged(nameof(DeleteModalMessage));
        OnPropertyChanged(nameof(ExitModalMessage));
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        var config = _botListViewModel.GetConfig();
        config.MinimizeToTray = value;
        _botListViewModel.SaveConfig();
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        var config = _botListViewModel.GetConfig();
        config.IsDarkTheme = value;
        _botListViewModel.SaveConfig();
    }

    partial void OnSelectedLanguageOptionChanged(LanguageOption value)
    {
        if (value is null) return;
        LocalizationService.Instance.SetLanguage(value.Code);
        var config = _botListViewModel.GetConfig();
        config.Language = value.Code;
        _botListViewModel.SaveConfig();
    }

    [RelayCommand]
    private void ToggleMinimizeToTray()
    {
        MinimizeToTray = !MinimizeToTray;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        if (Application.Current != null)
            Application.Current.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    [RelayCommand]
    private void OpenDiscord() => OpenUrl(DiscordUrl);

    [RelayCommand]
    private void OpenUpEkoRelease() => OpenUrl(UpEkoReleaseUrl);

    private void OpenUrl(string url)
    {
        try
        {
            PlatformSpecific.Open(url);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open {Url}", url);
            ShowToast(string.Format(Loc["Toast_OpenFailed"], ex.Message), ToastKind.Error);
        }
    }

    public void NavigateToList()
    {
        CurrentView = _botListViewModel;
    }

    public void NavigateToDetail(BotViewModel botViewModel)
    {
        CurrentView = botViewModel;
    }

    public void ShowToast(string message, ToastKind kind = ToastKind.Info)
    {
        ToastMessage = message;
        ToastKind = kind;
        IsToastVisible = true;

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer
        {
            // Errors stay longer, they usually need to be read.
            Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 8 : 3)
        };
        _toastTimer.Tick += (_, _) =>
        {
            IsToastVisible = false;
            _toastTimer?.Stop();
        };
        _toastTimer.Start();
    }

    [RelayCommand]
    private void DismissToast()
    {
        _toastTimer?.Stop();
        IsToastVisible = false;
    }

    public void RequestDelete(string botName)
    {
        DeleteModalBotName = botName;
        IsDeleteModalOpen = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsDeleteModalOpen = false;
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        IsDeleteModalOpen = false;
        if (CurrentView is BotViewModel detail)
        {
            _botListViewModel.RemoveBot(detail);
            NavigateToList();
            ShowToast(Loc["Toast_InstanceDeleted"], ToastKind.Success);
        }
    }

    /// <summary>
    /// Returns true when upeko can quit right away; otherwise shows the exit confirmation.
    /// </summary>
    public bool RequestExit()
    {
        var running = _botListViewModel.Bots.Count(b => b.IsRunning);
        if (running == 0)
            return true;

        RunningBotCount = running;
        IsExitModalOpen = true;
        return false;
    }

    [RelayCommand]
    private void CancelExit()
    {
        IsExitModalOpen = false;
    }

    [RelayCommand]
    private void ConfirmExit()
    {
        IsExitModalOpen = false;
        StopBotsForExit();
        CloseRequested?.Invoke();
    }

    public void StopBotsForExit() => _botListViewModel.StopAllForExit();

    [RelayCommand]
    private async Task InstallDep(string dep)
    {
        DepViewModel? vm = dep switch
        {
            "ffmpeg" => Ffmpeg,
            "ytdlp" => Ytdlp,
            "js" => Js,
            _ => null
        };

        if (vm is null || !vm.IsNotInstalled)
            return;

        ShowToast(string.Format(Loc["Toast_InstallingDep"], vm.DisplayName));
        var success = await vm.InstallAsync();
        if (success)
            ShowToast(string.Format(Loc["Toast_DepInstalled"], vm.DisplayName), ToastKind.Success);
        else
            ShowToast(vm.LastError ?? string.Format(Loc["Toast_DepInstallFailed"], vm.DisplayName), ToastKind.Error);
    }

    private async Task CheckForUpEkoUpdatesAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync("https://api.github.com/repos/nadeko-bot/upeko/releases/latest");
            response.EnsureSuccessStatusCode();

            var newRelease = await response.Content.ReadFromJsonAsync(SourceJsonSerializer.Default.ReleaseModel);

            if (newRelease != null)
            {
                var latestVersion = newRelease.TagName?.TrimStart('v') ?? "1.0.0";
                IsUpEkoUpdateAvailable = VersionUtil.Compare(CurrentVersion, latestVersion) < 0;
                if (IsUpEkoUpdateAvailable)
                    Log.Information("Upeko update available: {Latest} (current: {Current})", latestVersion, CurrentVersion);
                else
                    Log.Information("Upeko is up to date: {Current}", CurrentVersion);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking for Upeko updates");
            IsUpEkoUpdateAvailable = false;
        }
    }
}
