using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using upeko.Models;
using upeko.Services;

namespace upeko.ViewModels;

public partial class BotViewModel : ViewModelBase
{
    private readonly BotModel _bot = null!;
    public BotModel Bot => _bot;
    public BotListViewModel Parent { get; } = null!;

    [ObservableProperty]
    private MainActivityState _state;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressTitle = "Downloading...";

    [ObservableProperty]
    private string _progressDetail = "Initializing...";

    [ObservableProperty]
    private string _progressPercent = "0%";

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isEditingName;

    [ObservableProperty]
    private string _editedName = string.Empty;

    private Process? _process;

    public string BotPath
    {
        get => Bot.PathUri ?? string.Empty;
        set
        {
            _bot.PathUri = value;
            Parent.UpdateBot(_bot);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExecutablePath));
        }
    }

    public string Name => Bot.Name;
    public string DisplayInitial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpper();
    public string ExecutablePath => Path.Combine(BotPath, PlatformSpecific.GetExecutableName());
    public bool IsRunning => State == MainActivityState.Running;
    public bool IsReady => State == MainActivityState.Runnable || State == MainActivityState.Updatable;
    public bool IsMissing => State == MainActivityState.Downloadable;
    public bool ShowStartStop => IsRunning || IsReady;
    public bool IsUpdateAvailable => UpdateChecker.Instance.IsUpdateAvailable(Bot?.Version);
    public bool IsBotDownloaded => !string.IsNullOrWhiteSpace(Bot?.Version);
    public string UpdateButtonText => IsCheckingForUpdates ? "Checking..." : IsUpdateAvailable ? "Update" : "Check Updates";

    public string StatusText => State switch
    {
        MainActivityState.Running => "Running",
        MainActivityState.Runnable => "Ready",
        MainActivityState.Updatable => "Update Available",
        MainActivityState.Downloadable => "Not Downloaded",
        _ => "Unknown"
    };

    public string? Version => Bot?.Version;

    public BotViewModel()
    {
        UpdateChecker.Instance.OnDownloadProgress += OnDownloadProgress;
        UpdateChecker.Instance.OnDownloadComplete += OnDownloadComplete;
    }

    public BotViewModel(BotListViewModel parent, BotModel model) : this()
    {
        _bot = model;
        Parent = parent;
        ReloadVersionFromPath();
        UpdateCurrentActivity();
    }

    partial void OnStateChanged(MainActivityState value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(ShowStartStop));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(IsBotDownloaded));
    }

    partial void OnIsCheckingForUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(UpdateButtonText));
    }

    [RelayCommand]
    private void GoBack()
    {
        Parent?.RemoveNavigation();
    }

    [RelayCommand]
    private void StartBot()
    {
        if (State != MainActivityState.Runnable && State != MainActivityState.Updatable)
            return;

        if (string.IsNullOrEmpty(ExecutablePath) || !File.Exists(ExecutablePath))
            return;

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = BotPath,
        });

        _ = Task.Run(async () =>
        {
            if (_process is not null)
                await _process.WaitForExitAsync();
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _process = null;
                UpdateCurrentActivity();
            });
            _process?.Dispose();
        });

        UpdateCurrentActivity();
    }

    [RelayCommand]
    private void StopBot()
    {
        if (State != MainActivityState.Running)
            return;

        using var p = _process;
        _process = null;
        try { p?.Kill(); } catch { }
        UpdateCurrentActivity();
    }

    [RelayCommand]
    private async Task UpdateBot()
    {
        if (!IsUpdateAvailable && IsBotDownloaded)
        {
            await CheckForUpdatesAsync();
        }
        else
        {
            await DownloadAndInstallBotAsync();
        }
    }

    [RelayCommand]
    private void DownloadBot()
    {
        _ = DownloadAndInstallBotAsync();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (string.IsNullOrWhiteSpace(BotPath))
            return;

        var dataFolder = Path.Combine(BotPath, "data");
        if (!Directory.Exists(dataFolder))
            return;

        Process.Start(PlatformSpecific.GetNameOfFileExplorer(), dataFolder);
    }

    [RelayCommand]
    private void OpenCreds()
    {
        if (string.IsNullOrWhiteSpace(BotPath))
            return;

        var credsFile = Path.Combine(BotPath, "data", "creds.yml");
        var credsExampleFile = Path.Combine(BotPath, "data", "creds_example.yml");
        if (!File.Exists(credsFile))
        {
            if (!File.Exists(credsExampleFile))
                return;
            File.Copy(credsExampleFile, credsFile);
        }

        Process.Start(PlatformSpecific.GetNameOfFileExplorer(), Path.GetFullPath(credsFile));
    }

    [RelayCommand]
    private async Task BrowsePath()
    {
        var topLevel = TopLevel.GetTopLevel(App.MainWindow);
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Bot Path",
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            BotPath = result[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task ChangeAvatar()
    {
        var topLevel = TopLevel.GetTopLevel(App.MainWindow);
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Avatar Image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Image Files")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif" }
                }
            }
        });

        if (result.Count > 0)
        {
            _bot.IconUri = new Uri(result[0].Path.LocalPath).ToString();
            Parent?.UpdateBot(Bot);
            OnPropertyChanged(nameof(Bot));
        }
    }

    [RelayCommand]
    private void ShowDeleteModal()
    {
        Parent?.RequestDelete(Bot.Name);
    }

    [RelayCommand]
    private void EditName()
    {
        EditedName = Name;
        IsEditingName = true;
    }

    [RelayCommand]
    private void SaveName()
    {
        if (!string.IsNullOrWhiteSpace(EditedName) && EditedName != Name)
        {
            _bot.Name = EditedName;
            Parent.UpdateBot(_bot);
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(DisplayInitial));
        }

        IsEditingName = false;
    }

    [RelayCommand]
    private void CancelEditName()
    {
        EditedName = Name;
        IsEditingName = false;
    }

    public void ReloadVersionFromPath()
    {
        if (string.IsNullOrWhiteSpace(BotPath))
            return;

        if (!File.Exists(ExecutablePath))
        {
            Bot.Version = null;
            return;
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });

            var info = p?.StandardOutput.ReadToEnd();
            Bot.Version = info?.Trim();
        }
        catch
        {
            Bot.Version = null;
        }

        UpdateCurrentActivity();
    }

    private void UpdateCurrentActivity()
    {
        if (!IsBotDownloaded)
            State = MainActivityState.Downloadable;
        else if (_process != null)
            State = MainActivityState.Running;
        else if (IsUpdateAvailable)
            State = MainActivityState.Updatable;
        else
            State = MainActivityState.Runnable;
    }

    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        try
        {
            await UpdateChecker.Instance.CheckForUpdatesAsync();
            UpdateCurrentActivity();
            OnPropertyChanged(nameof(UpdateButtonText));
            OnPropertyChanged(nameof(IsUpdateAvailable));
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private async Task DownloadAndInstallBotAsync()
    {
        try
        {
            IsProgressVisible = true;
            ProgressValue = 0;
            ProgressPercent = "0%";
            ProgressTitle = "Downloading...";
            ProgressDetail = "Please wait, do not close the application.";

            await UpdateChecker.Instance.DownloadAndInstallBotAsync(Bot.Name, BotPath);
        }
        catch (Exception ex)
        {
            IsProgressVisible = false;
            ProgressDetail = $"Error: {ex.Message}";
        }
    }

    private void OnDownloadProgress(double progress, string status)
    {
        IsProgressVisible = true;
        ProgressValue = progress * 100;
        ProgressPercent = $"{(int)(progress * 100)}%";
        ProgressTitle = status;
    }

    private void OnDownloadComplete(bool success, string version)
    {
        IsProgressVisible = false;
        if (success)
        {
            ReloadVersionFromPath();
            UpdateCurrentActivity();
        }
        else
        {
            ProgressDetail = $"Download failed: {version}";
        }
    }
}
