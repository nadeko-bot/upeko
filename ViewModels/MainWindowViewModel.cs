using System;
using System.Collections.Generic;
using System.Diagnostics;
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

public partial class MainWindowViewModel : ViewModelBase
{
    private const string ChangelogUrl = "https://github.com/nadeko-bot/nadekobot/blob/v6/CHANGELOG.md";
    private const string UpEkoReleaseUrl = "https://github.com/nadeko-bot/upeko/releases";

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
    private string _toastType = "info";

    [ObservableProperty]
    private string _ffmpegStatus = "checking";

    [ObservableProperty]
    private string _ytdlpStatus = "checking";

    [ObservableProperty]
    private string _jsStatus = "checking";

    [ObservableProperty]
    private bool _isDeleteModalOpen;

    [ObservableProperty]
    private string _deleteModalBotName = string.Empty;

    [ObservableProperty]
    private bool _isExitModalOpen;

    [ObservableProperty]
    private int _runningBotCount;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private LanguageOption _selectedLanguageOption;

    public List<LanguageOption> LanguageOptions { get; } = LocalizationService.Languages
        .Select(kv => new LanguageOption(kv.Key, kv.Value))
        .ToList();

    private readonly BotListViewModel _botListViewModel;
    private readonly FfmpegDepViewModel _ffmpegViewModel;
    private readonly YtdlDepViewModel _ytdlpViewModel;
    private readonly JsDepViewModel _jsDepViewModel;
    private DispatcherTimer? _toastTimer;
    private readonly string _currentVersion;

    public string CurrentVersion => _currentVersion;

    public string DeleteModalMessage =>
        $"{Loc["Modal_DeletePrefix"]}{DeleteModalBotName}{Loc["Modal_DeleteSuffix"]}";

    public string ExitModalMessage =>
        $"{RunningBotCount}{Loc["Modal_ExitSuffix"]}";

    /// <summary>
    /// Raised when the ViewModel wants the window to close.
    /// MainWindow subscribes to this in OnDataContextChanged.
    /// </summary>
    public Action? CloseRequested { get; set; }

    public ConfigModel GetConfig() => _botListViewModel.GetConfig();

    public MainWindowViewModel()
    {
        _ffmpegViewModel = new FfmpegDepViewModel();
        _ytdlpViewModel = new YtdlDepViewModel();
        _jsDepViewModel = new JsDepViewModel();

        _botListViewModel = new BotListViewModel(this);
        _currentView = _botListViewModel;
        _minimizeToTray = _botListViewModel.GetConfig().MinimizeToTray;
        _selectedLanguageOption = LanguageOptions.FirstOrDefault(l => l.Code == _botListViewModel.GetConfig().Language)
            ?? LanguageOptions.First();

        _currentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0";

        _isDarkTheme = _botListViewModel.GetConfig().IsDarkTheme;
        if (Application.Current != null)
            Application.Current.RequestedThemeVariant = _isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;

        _ffmpegViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DepViewModel.StatusString))
                FfmpegStatus = _ffmpegViewModel.StatusString;
        };
        _ytdlpViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DepViewModel.StatusString))
                YtdlpStatus = _ytdlpViewModel.StatusString;
        };
        _jsDepViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DepViewModel.StatusString))
                JsStatus = _jsDepViewModel.StatusString;
        };

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(1000);
            await _ffmpegViewModel.CheckAsync();
            await _ytdlpViewModel.CheckAsync();
            await _jsDepViewModel.CheckAsync();

            await CheckForUpEkoUpdatesAsync();
        });
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

    partial void OnDeleteModalBotNameChanged(string value)
    {
        OnPropertyChanged(nameof(DeleteModalMessage));
    }

    partial void OnRunningBotCountChanged(int value)
    {
        OnPropertyChanged(nameof(ExitModalMessage));
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
    private void OpenChangelog()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ChangelogUrl,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenDiscord()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://discord.gg/nadekobot",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenUpEkoRelease()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = UpEkoReleaseUrl,
            UseShellExecute = true
        });
    }

    public void NavigateToList()
    {
        CurrentView = _botListViewModel;
    }

    public void NavigateToDetail(BotViewModel botViewModel)
    {
        CurrentView = botViewModel;
    }

    public void ShowToast(string message, string type = "info")
    {
        ToastMessage = message;
        ToastType = type;
        IsToastVisible = true;

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            IsToastVisible = false;
            _toastTimer.Stop();
        };
        _toastTimer.Start();
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
            ShowToast(Loc["Toast_InstanceDeleted"], "success");
        }
    }

    public bool RequestExit()
    {
        var running = 0;
        foreach (var bot in _botListViewModel.Bots)
        {
            if (bot.IsRunning)
                running++;
        }

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
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void InstallDep(string dep)
    {
        if (dep == "ffmpeg" && _ffmpegViewModel.IsNotInstalled)
        {
            ShowToast(Loc["Toast_InstallingFfmpeg"], "info");
            _ = _ffmpegViewModel.InstallAsync();
        }
        else if (dep == "ytdlp" && _ytdlpViewModel.IsNotInstalled)
        {
            ShowToast(Loc["Toast_InstallingYtdlp"], "info");
            _ = _ytdlpViewModel.InstallAsync();
        }
        else if (dep == "js" && _jsDepViewModel.IsNotInstalled)
        {
            ShowToast(Loc["Toast_InstallingJs"], "info");
            _ = _jsDepViewModel.InstallAsync();
        }
    }

    private async Task CheckForUpEkoUpdatesAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://api.github.com/repos/nadeko-bot/upeko/releases/latest");
            response.EnsureSuccessStatusCode();

            var newRelease = await response.Content.ReadFromJsonAsync(SourceJsonSerializer.Default.ReleaseModel);

            if (newRelease != null)
            {
                var latestVersion = newRelease.TagName?.TrimStart('v') ?? "1.0.0.0";
                IsUpEkoUpdateAvailable = CompareVersions(_currentVersion, latestVersion) < 0;
                if (IsUpEkoUpdateAvailable)
                    Log.Information("Upeko update available: {Latest} (current: {Current})", latestVersion, _currentVersion);
                else
                    Log.Information("Upeko is up to date: {Current}", _currentVersion);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking for Upeko updates");
            IsUpEkoUpdateAvailable = false;
        }
    }

    private int CompareVersions(string version1, string version2)
    {
        if (Version.TryParse(version1, out var v1) && Version.TryParse(version2, out var v2))
        {
            return v1.CompareTo(v2);
        }

        return string.Compare(version1, version2, StringComparison.Ordinal);
    }
}
