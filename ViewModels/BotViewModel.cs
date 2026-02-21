using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using upeko.Models;
using upeko.Services;

namespace upeko.ViewModels;

public partial class BotViewModel : ViewModelBase
{
    private readonly BotModel _bot = null!;
    private bool _isDownloading;
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
    private string? _diskSpaceInfo;

    [ObservableProperty]
    private bool _isEditingName;

    [ObservableProperty]
    private string _editedName = string.Empty;

    [ObservableProperty]
    private string _downloadSpeed = string.Empty;

    [ObservableProperty]
    private string _downloadEta = string.Empty;

    [ObservableProperty]
    private string _downloadBytes = string.Empty;

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
    public bool IsUpdateAvailable => IsBotDownloaded && UpdateChecker.Instance.IsUpdateAvailable(Bot?.Version);
    public bool IsBotDownloaded => !string.IsNullOrWhiteSpace(Bot?.Version);
    public string UpdateButtonText => IsCheckingForUpdates ? Loc["BotView_Checking"] : IsUpdateAvailable ? Loc["BotView_UpdateButton"] : Loc["BotView_CheckUpdates"];

    public string StatusText => State switch
    {
        MainActivityState.Running => Loc["Status_Running"],
        MainActivityState.Runnable => Loc["Status_Ready"],
        MainActivityState.Updatable => Loc["Status_UpdateAvailable"],
        MainActivityState.Downloadable => Loc["Status_NotDownloaded"],
        _ => Loc["Status_Unknown"]
    };

    public string? Version => Bot?.Version;

    public BotViewModel()
    {
        UpdateChecker.Instance.OnDownloadProgress += OnDownloadProgress;
        UpdateChecker.Instance.OnDownloadComplete += OnDownloadComplete;
        UpdateChecker.Instance.OnDownloadCancelled += OnDownloadCancelled;

        LocalizationService.Instance.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(UpdateButtonText));
            OnPropertyChanged(nameof(Loc));
        };
    }

    public BotViewModel(BotListViewModel parent, BotModel model) : this()
    {
        _bot = model;
        Parent = parent;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await ReloadVersionFromPathAsync();
        _ = InitDiskSpaceAsync();
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

        Log.Information("Starting bot {BotName}", Bot.Name);
        _process = Process.Start(new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = BotPath,
        });

        _bot.WasRunning = true;
        Parent.UpdateBot(_bot);

        _ = Task.Run(async () =>
        {
            if (_process is not null)
                await _process.WaitForExitAsync();
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _process = null;

                _bot.WasRunning = false;
                Parent?.UpdateBot(_bot);

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

        Log.Information("Stopping bot {BotName}", Bot.Name);
        _bot.WasRunning = false;
        Parent.UpdateBot(_bot);

        using var p = _process;
        _process = null;
        try { p?.Kill(); }
        catch (Exception ex) { Log.Warning(ex, "Failed to kill bot process {BotName}", Bot.Name); }
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
    private void CancelDownload()
    {
        Log.Information("Cancelling download for {BotName}", Bot.Name);
        UpdateChecker.Instance.CancelDownload();
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
            Title = Loc["BotView_SelectBotPath"],
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
            Title = Loc["BotView_SelectAvatarImage"],
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Loc["BotView_ImageFiles"])
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

    public async Task ReloadVersionFromPathAsync()
    {
        if (string.IsNullOrWhiteSpace(BotPath))
            return;

        if (!File.Exists(ExecutablePath))
        {
            Bot.Version = null;
            OnPropertyChanged(nameof(Version));
            UpdateCurrentActivity();
            return;
        }

        try
        {
            var version = await Task.Run(async () =>
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = ExecutablePath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                });

                if (p is null) return null;

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                var info = await p.StandardOutput.ReadToEndAsync(cts.Token);
                await p.WaitForExitAsync(cts.Token);
                return info?.Trim();
            });

            Bot.Version = version;
            if (version is not null)
                Log.Information("Detected bot version {Version} for {BotName}", version, Bot.Name);
        }
        catch
        {
            Bot.Version = null;
        }

        OnPropertyChanged(nameof(Version));
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
            _ = UpdateDiskSpaceInfoAsync();
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private async Task InitDiskSpaceAsync()
    {
        if (UpdateChecker.Instance.LatestRelease is null)
            await UpdateChecker.Instance.CheckForUpdatesAsync();
        await UpdateDiskSpaceInfoAsync();
    }

    private async Task UpdateDiskSpaceInfoAsync()
    {
        try
        {
            var info = await Task.Run(() =>
            {
                var release = UpdateChecker.Instance.LatestRelease;
                if (release?.Assets is null || release.Assets.Length == 0)
                    return null;

                var assetName = $"{PlatformSpecific.GetOS()}-{PlatformSpecific.GetArchitecture()}";
                var asset = Array.Find(release.Assets, a => a.Name?.Contains(assetName) == true);
                if (asset is null || asset.Size <= 0)
                    return null;

                var requiredBytes = asset.Size * 4 + 100_000_000L;
                var fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(BotPath) ? "." : BotPath);

                var availableBytes = DiskSpace.GetAvailableBytes(fullPath);
                if (availableBytes is null)
                    return null;

                return new { Required = requiredBytes, Available = availableBytes.Value };
            });

            if (info is null)
            {
                DiskSpaceInfo = null;
                return;
            }

            DiskSpaceInfo = $"~{FormatBytes(info.Required)} required  ·  {FormatBytes(info.Available)} available";
        }
        catch
        {
            DiskSpaceInfo = null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F0} MB",
            _ => $"{bytes / 1024.0:F0} KB"
        };
    }

    private async Task DownloadAndInstallBotAsync()
    {
        try
        {
            Log.Information("Starting download for {BotName} at {BotPath}", Bot.Name, BotPath);
            _isDownloading = true;
            IsProgressVisible = true;
            ProgressValue = 0;
            ProgressPercent = "0%";
            ProgressTitle = Loc["Progress_Downloading"];
            ProgressDetail = Loc["Progress_PleaseWait"];

            await UpdateChecker.Instance.DownloadAndInstallBotAsync(Bot.Name, BotPath);
        }
        catch (Exception ex)
        {
            _isDownloading = false;
            IsProgressVisible = false;
            ProgressDetail = $"Error: {ex.Message}";
        }
    }

    private void OnDownloadProgress(DownloadProgressInfo info)
    {
        if (!_isDownloading) return;
        IsProgressVisible = true;
        ProgressValue = info.Progress * 100;
        ProgressPercent = $"{(int)(info.Progress * 100)}%";
        ProgressTitle = info.Status;
        DownloadSpeed = FormatSpeed(info.SpeedBytesPerSec);
        DownloadBytes = FormatBytes(info.BytesDownloaded, info.TotalBytes);
        DownloadEta = FormatEta(info.EtaSeconds);
    }

    private async void OnDownloadComplete(bool success, string version)
    {
        if (!_isDownloading) return;
        _isDownloading = false;
        IsProgressVisible = false;
        DownloadSpeed = string.Empty;
        DownloadEta = string.Empty;
        DownloadBytes = string.Empty;
        if (success)
        {
            Log.Information("Download completed for {BotName}", Bot.Name);
            await ReloadVersionFromPathAsync();
        }
        else
        {
            Log.Error("Download failed for {BotName}: {Error}", Bot.Name, version);
            ProgressDetail = $"Download failed: {version}";
        }
    }

    private string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return Loc["Progress_Stalled"];
        return bytesPerSec >= 1_048_576
            ? $"{bytesPerSec / 1_048_576:F1} MB/s"
            : $"{bytesPerSec / 1_024:F0} KB/s";
    }

    private static string FormatBytes(long downloaded, long? total)
    {
        var dl = downloaded / 1_048_576.0;
        if (!total.HasValue) return $"{dl:F1} MB";
        var tot = total.Value / 1_048_576.0;
        return $"{dl:F1} / {tot:F1} MB";
    }

    private string FormatEta(double? etaSeconds)
    {
        if (!etaSeconds.HasValue) return Loc["Progress_Calculating"];
        var s = (int)etaSeconds.Value;
        if (s < 0) return Loc["Progress_Calculating"];
        if (s < 60) return $"{s}s";
        if (s < 3600) return $"{s / 60}m {s % 60}s";
        return $"{s / 3600}h {s % 3600 / 60}m";
    }

    private void OnDownloadCancelled()
    {
        if (!_isDownloading) return;
        Log.Information("Download cancelled for {BotName}", Bot.Name);
        _isDownloading = false;
        IsProgressVisible = false;
        UpdateCurrentActivity();
    }
}
