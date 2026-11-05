using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using upeko.Services;

namespace upeko.ViewModels
{
    /// <summary>
    /// A program the bot's music features need (ffmpeg, yt-dlp, a JavaScript runtime).
    /// </summary>
    public abstract partial class DepViewModel : ViewModelBase
    {
        protected static readonly HttpClient Http = new()
        {
            DefaultRequestHeaders = { { "User-Agent", "Upeko" } },
            Timeout = TimeSpan.FromMinutes(10),
        };

        public string DisplayName { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsChecking), nameof(IsNotInstalled), nameof(IsInstalled),
            nameof(StatusString), nameof(Tooltip))]
        private DepState _state;

        public bool IsChecking => State == DepState.Checking;
        public bool IsNotInstalled => State == DepState.NotInstalled;
        public bool IsInstalled => State == DepState.Installed;

        /// <summary>
        /// Why the last install failed, when there is something more useful to say than "it failed".
        /// </summary>
        public string? LastError { get; protected set; }

        public string StatusString => State switch
        {
            DepState.Installed => "installed",
            DepState.NotInstalled => "missing",
            _ => "checking"
        };

        public string Tooltip => State switch
        {
            DepState.Installed => string.Format(Loc["Dep_TipInstalled"], DisplayName),
            DepState.NotInstalled => string.Format(Loc["Dep_TipMissing"], DisplayName),
            _ => string.Format(Loc["Dep_TipChecking"], DisplayName),
        };

        protected DepViewModel(string displayName)
        {
            DisplayName = displayName;
            State = DepState.Checking;
            LocalizationService.Instance.LanguageChanged += () => OnPropertyChanged(nameof(Tooltip));
        }

        public async Task<bool> InstallAsync()
        {
            if (State != DepState.NotInstalled)
                return false;

            Log.Information("Installing dependency: {Name}", DisplayName);
            LastError = null;
            State = DepState.Checking;

            bool success;
            try
            {
                success = await InternalInstallAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to install {Name}", DisplayName);
                success = false;
            }

            State = success ? DepState.Installed : DepState.NotInstalled;
            Log.Information("Dependency {Name} install result: {Status}", DisplayName, StatusString);
            return success;
        }

        public async Task CheckAsync()
        {
            State = DepState.Checking;
            DepState newState;
            try
            {
                newState = await InternalCheckAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to check {Name}", DisplayName);
                newState = DepState.NotInstalled;
            }

            State = newState;
            Log.Information("Dependency {Name}: {Status}", DisplayName, StatusString);
        }

        protected abstract Task<DepState> InternalCheckAsync();
        protected abstract Task<bool> InternalInstallAsync();

        /// <summary>
        /// True if the program starts. A program that is still running after the timeout counts as found.
        /// </summary>
        protected static async Task<bool> CanRunAsync(string fileName, TimeSpan timeout, params string[] args)
        {
            var psi = PlatformSpecific.ToolStartInfo(fileName, args);
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            Process? process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Win32Exception)
            {
                return false;
            }

            if (process is null)
                return false;

            using (process)
            {
                // Drain the output so a chatty tool cannot block on a full pipe.
                process.OutputDataReceived += (_, _) => { };
                process.ErrorDataReceived += (_, _) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var cts = new CancellationTokenSource(timeout);
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }

            return true;
        }

        protected static async Task DownloadToFileAsync(string url, string path)
        {
            await using var stream = await Http.GetStreamAsync(url);
            await using var fs = new FileStream(path, FileMode.Create);
            await stream.CopyToAsync(fs);
        }
    }
}

public enum DepState
{
    Checking,
    NotInstalled,
    Installed,
}
