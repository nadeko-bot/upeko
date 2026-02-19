using System;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using upeko.Models;
using upeko.Services;

namespace upeko.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string ChangelogUrl = "https://github.com/nadeko-bot/nadekobot/blob/v6/CHANGELOG.md";
    private const string UpEkoReleaseUrl = "https://github.com/nadeko-bot/upeko/releases";

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
    private bool _isDeleteModalOpen;

    [ObservableProperty]
    private string _deleteModalBotName = string.Empty;

    private readonly BotListViewModel _botListViewModel;
    private readonly FfmpegDepViewModel _ffmpegViewModel;
    private readonly YtdlDepViewModel _ytdlpViewModel;
    private DispatcherTimer? _toastTimer;
    private readonly string _currentVersion;

    public string CurrentVersion => _currentVersion;

    public MainWindowViewModel()
    {
        _ffmpegViewModel = new FfmpegDepViewModel();
        _ytdlpViewModel = new YtdlDepViewModel();

        _botListViewModel = new BotListViewModel(this);
        _currentView = _botListViewModel;

        _currentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0";

        var currentVariant = Application.Current?.ActualThemeVariant;
        _isDarkTheme = currentVariant == ThemeVariant.Dark;

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

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(1000);
            await _ffmpegViewModel.CheckAsync();
            await _ytdlpViewModel.CheckAsync();

            await CheckForUpEkoUpdatesAsync();
        });
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }
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
            ShowToast("Instance deleted", "success");
        }
    }

    [RelayCommand]
    private void InstallDep(string dep)
    {
        if (dep == "ffmpeg" && _ffmpegViewModel.IsNotInstalled)
        {
            ShowToast("Installing ffmpeg...", "info");
            _ = _ffmpegViewModel.InstallAsync();
        }
        else if (dep == "ytdlp" && _ytdlpViewModel.IsNotInstalled)
        {
            ShowToast("Installing yt-dlp...", "info");
            _ = _ytdlpViewModel.InstallAsync();
        }
    }

    private async Task CheckForUpEkoUpdatesAsync()
    {
        try
        {
            var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Upeko-Update-Checker");

            var response = await httpClient.GetAsync("https://api.github.com/repos/nadeko-bot/upeko/releases/latest");
            response.EnsureSuccessStatusCode();

            var newRelease = await response.Content.ReadFromJsonAsync(SourceJsonSerializer.Default.ReleaseModel);

            if (newRelease != null)
            {
                var latestVersion = newRelease.TagName?.TrimStart('v') ?? "1.0.0.0";
                IsUpEkoUpdateAvailable = CompareVersions(_currentVersion, latestVersion) < 0;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error checking for updates: {ex.Message}");
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
