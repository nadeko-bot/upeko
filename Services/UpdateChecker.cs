using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using upeko.Models;

namespace upeko.Services
{
    public record DownloadResult(bool Success, bool Cancelled, string? Error)
    {
        public static DownloadResult Ok() => new(true, false, null);
        public static DownloadResult WasCancelled() => new(false, true, null);
        public static DownloadResult Failed(string error) => new(false, false, error);
    }

    /// <summary>
    /// Checks GitHub for NadekoBot releases and downloads and installs them.
    /// </summary>
    public class UpdateChecker
    {
        public static UpdateChecker Instance { get; } = new();

        private const string GitHubApiUrl = "https://api.github.com/repos/nadeko-bot/nadekobot/releases/latest";

        private static LocalizationService Loc => LocalizationService.Instance;

        private readonly HttpClient _httpClient;
        private Task<string?>? _checkTask;
        private CancellationTokenSource? _downloadCts;

        /// <summary>
        /// The latest available release from GitHub.
        /// </summary>
        public ReleaseModel? LatestRelease { get; private set; }

        /// <summary>
        /// The latest available version of the bot.
        /// </summary>
        public string LatestVersion
            => LatestRelease?.TagName?.TrimStart('v') ?? "1.0.0";

        /// <summary>
        /// Raised when a check finds a release that differs from the one known before.
        /// </summary>
        public event Action<ReleaseModel>? OnNewVersionFound;

        /// <summary>
        /// True while a bot is being downloaded. Only one download runs at a time.
        /// </summary>
        public bool IsDownloading => _downloadCts is not null;

        private UpdateChecker()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Upeko-Bot-Updater");
        }

        /// <summary>
        /// Checks if a newer version is available for the specified current version.
        /// </summary>
        public bool IsUpdateAvailable(string? currentVersion)
        {
            if (currentVersion == null)
                return true;

            return VersionUtil.Compare(currentVersion, LatestVersion) < 0;
        }

        /// <summary>
        /// Checks for updates from GitHub API. Concurrent callers share one request.
        /// </summary>
        /// <returns>Null if successful, error message if failed.</returns>
        public Task<string?> CheckForUpdatesAsync()
        {
            if (_checkTask is { IsCompleted: false })
                return _checkTask;

            _checkTask = CheckForUpdatesCore();
            return _checkTask;
        }

        private async Task<string?> CheckForUpdatesCore()
        {
            try
            {
                using var response = await _httpClient.GetAsync(GitHubApiUrl);
                response.EnsureSuccessStatusCode();

                var newRelease = await response.Content.ReadFromJsonAsync(SourceJsonSerializer.Default.ReleaseModel);
                if (newRelease != null && (LatestRelease == null || LatestRelease.TagName != newRelease.TagName))
                {
                    LatestRelease = newRelease;
                    Log.Information("New NadekoBot version found: {Version}", newRelease.TagName);
                    OnNewVersionFound?.Invoke(newRelease);
                }

                Log.Information("Checked for NadekoBot updates, latest: {Version}", LatestVersion);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check for NadekoBot updates");
                return ex.Message;
            }
        }

        /// <summary>
        /// The release asset for this OS and architecture, if the latest release has one.
        /// </summary>
        public ReleaseAsset? FindAssetForThisPlatform()
        {
            var name = GetAssetName();
            return Array.Find(LatestRelease?.Assets ?? [],
                a => a.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
        }

        private static string GetAssetName()
        {
            var os = PlatformSpecific.GetOS();
            var extension = os == "win" ? ".zip" : ".tar.gz";
            return $"nadeko-{os}-{PlatformSpecific.GetArchitecture()}{extension}";
        }

        public void CancelDownload()
        {
            _downloadCts?.Cancel();
        }

        public async Task<DownloadResult> DownloadAndInstallBotAsync(
            string botName,
            string botPath,
            Action<DownloadProgressInfo> onProgress)
        {
            if (string.IsNullOrWhiteSpace(botPath))
                throw new ArgumentNullException(nameof(botPath));

            if (_downloadCts is not null)
                return DownloadResult.Failed(Loc["Error_DownloadInProgress"]);

            _downloadCts = new CancellationTokenSource();
            var ct = _downloadCts.Token;
            string? tempDir = null;

            try
            {
                if (LatestRelease == null)
                {
                    var error = await CheckForUpdatesAsync();
                    if (error != null)
                        return DownloadResult.Failed(error);
                }

                if (LatestRelease?.Assets == null || LatestRelease.Assets.Length == 0)
                    return DownloadResult.Failed(Loc["Error_NoAssets"]);

                var os = PlatformSpecific.GetOS();
                var arch = PlatformSpecific.GetArchitecture();
                var assetName = GetAssetName();
                var asset = FindAssetForThisPlatform();

                if (asset == null || string.IsNullOrEmpty(asset.DownloadUrl))
                    return DownloadResult.Failed(string.Format(Loc["Error_NoAssetForPlatform"], assetName));

                botPath = Path.GetFullPath(botPath);
                var parentDir = Path.GetDirectoryName(botPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                // The old install is moved next to the bot folder and deleted later,
                // so a bot in a drive root would take the whole drive with it.
                if (string.IsNullOrEmpty(parentDir))
                    return DownloadResult.Failed(Loc["Error_RootFolder"]);

                var requiredBytes = DiskSpace.RequiredForInstall(asset.Size);
                var availableBytes = DiskSpace.GetAvailableBytes(botPath);
                if (availableBytes is not null && availableBytes < requiredBytes)
                {
                    return DownloadResult.Failed(string.Format(Loc["Error_NotEnoughSpace"],
                        DiskSpace.Format(requiredBytes), DiskSpace.Format(availableBytes.Value)));
                }

                Directory.CreateDirectory(parentDir);
                CleanupOrphanedTempDirs(parentDir);

                // Download and extract next to the bot folder, because Directory.Move
                // does not work across drives.
                tempDir = Path.Combine(parentDir, ".dl-" + Guid.NewGuid());
                Directory.CreateDirectory(tempDir);

                var downloadingText = string.Format(Loc["Progress_DownloadingVersion"], LatestRelease.TagName);
                var downloadPath = Path.Combine(tempDir, assetName);
                onProgress(new DownloadProgressInfo(0, 0, 0, null, null, downloadingText));

                await DownloadFileAsync(asset.DownloadUrl, downloadPath, downloadingText, onProgress, ct);

                onProgress(new DownloadProgressInfo(1, 0, 0, null, null, Loc["Progress_Extracting"]));

                var extractPath = Path.Combine(tempDir, "extract");
                Directory.CreateDirectory(extractPath);

                ct.ThrowIfCancellationRequested();

                if (os == "win")
                {
                    await Task.Run(() => ZipFile.ExtractToDirectory(downloadPath, extractPath), ct);
                }
                else
                {
                    var tarError = await ExtractTarGzAsync(downloadPath, extractPath, ct);
                    if (tarError is not null)
                        return DownloadResult.Failed(tarError);

                    PlatformSpecific.MakeExecutable(Path.Combine(extractPath, $"nadeko-{os}-{arch}", "NadekoBot"));
                }

                // Past this point the old install is touched, so the download can no longer be cancelled.
                ct.ThrowIfCancellationRequested();
                onProgress(new DownloadProgressInfo(1, 0, 0, null, null, Loc["Progress_Installing"]));

                InstallExtractedRelease(Path.Combine(extractPath, $"nadeko-{os}-{arch}"), botPath, botName);

                CleanupTempDir(tempDir);
                return DownloadResult.Ok();
            }
            catch (OperationCanceledException)
            {
                CleanupTempDir(tempDir);
                return DownloadResult.WasCancelled();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to download or install NadekoBot to {BotPath}", botPath);
                CleanupTempDir(tempDir);
                return DownloadResult.Failed(ex.Message);
            }
            finally
            {
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }

        private async Task DownloadFileAsync(
            string url,
            string downloadPath,
            string status,
            Action<DownloadProgressInfo> onProgress,
            CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalBytesRead = 0;
            int bytesRead;

            var sw = Stopwatch.StartNew();
            double smoothedSpeed = 0;
            long lastReportBytes = 0;
            double lastReportTime = 0;
            const double emaAlpha = 0.3;
            const double updateIntervalSec = 0.5;
            const double warmupSec = 3.0;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalBytesRead += bytesRead;

                var elapsed = sw.Elapsed.TotalSeconds;
                if (elapsed - lastReportTime < updateIntervalSec)
                    continue;

                var instantSpeed = (totalBytesRead - lastReportBytes) / (elapsed - lastReportTime);
                smoothedSpeed = smoothedSpeed == 0
                    ? instantSpeed
                    : emaAlpha * instantSpeed + (1 - emaAlpha) * smoothedSpeed;

                lastReportBytes = totalBytesRead;
                lastReportTime = elapsed;

                var progress = totalBytes.HasValue ? (double)totalBytesRead / totalBytes.Value : 0;

                double? eta = null;
                if (elapsed >= warmupSec && smoothedSpeed > 0 && totalBytes.HasValue)
                    eta = (totalBytes.Value - totalBytesRead) / smoothedSpeed;

                onProgress(new DownloadProgressInfo(progress, smoothedSpeed, totalBytesRead, totalBytes, eta, status));
            }
        }

        private static async Task<string?> ExtractTarGzAsync(string archivePath, string extractPath, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "tar",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-xzf");
            psi.ArgumentList.Add(archivePath);
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(extractPath);

            using var process = Process.Start(psi)!;
            var error = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return process.ExitCode == 0
                ? null
                : string.Format(Loc["Error_Extract"], error.Trim());
        }

        /// <summary>
        /// Swaps the new release into <paramref name="botPath"/>, keeping the user's data folder
        /// but taking data/lib from the new release. The old install is kept as .old-{botName}
        /// and moved back if the swap fails.
        /// </summary>
        private static void InstallExtractedRelease(string releaseDir, string botPath, string botName)
        {
            var safeName = string.Join("_", botName.Split(Path.GetInvalidFileNameChars()));
            var backupPath = Path.Combine(Path.GetDirectoryName(botPath)!, $".old-{safeName}");

            if (Directory.Exists(backupPath))
                Directory.Delete(backupPath, true);

            var hadOldInstall = Directory.Exists(botPath);
            if (hadOldInstall)
                Directory.Move(botPath, backupPath);

            try
            {
                Directory.Move(releaseDir, botPath);
            }
            catch
            {
                if (hadOldInstall && !Directory.Exists(botPath))
                    Directory.Move(backupPath, botPath);
                throw;
            }

            var oldDataPath = Path.Combine(backupPath, "data");
            var newDataPath = Path.Combine(botPath, "data");

            // Keep the fresh data/lib from the new release; the user's copy may hold outdated libraries.
            var newLibPath = Path.Combine(newDataPath, "lib");
            string? tempLibPath = null;
            if (Directory.Exists(newLibPath))
            {
                tempLibPath = Path.Combine(botPath, ".tmp-lib");
                Directory.Move(newLibPath, tempLibPath);
            }

            if (hadOldInstall && Directory.Exists(oldDataPath))
                CopyDirectory(oldDataPath, newDataPath);

            if (tempLibPath != null && Directory.Exists(tempLibPath))
            {
                if (Directory.Exists(newLibPath))
                    Directory.Delete(newLibPath, true);
                Directory.Move(tempLibPath, newLibPath);
            }
        }

        private static void CleanupTempDir(string? tempDir)
        {
            if (tempDir is null || !Directory.Exists(tempDir))
                return;
            try { Directory.Delete(tempDir, true); } catch { }
        }

        /// <summary>
        /// Removes download folders that a crash or a killed upeko left behind.
        /// </summary>
        private static void CleanupOrphanedTempDirs(string botsParentDir)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(botsParentDir, ".dl-*"))
                    CleanupTempDir(dir);
            }
            catch { }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);

            foreach (var dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
        }
    }

    public static class VersionUtil
    {
        /// <summary>
        /// Compares two "X.Y.Z" version strings; falls back to ordinal comparison when either does not parse.
        /// </summary>
        public static int Compare(string version1, string version2)
        {
            if (Version.TryParse(version1, out var v1) && Version.TryParse(version2, out var v2))
                return Normalize(v1).CompareTo(Normalize(v2));

            return string.Compare(version1, version2, StringComparison.Ordinal);
        }

        // Version treats "1.2.3" as lower than "1.2.3.0"; count missing parts as zero.
        private static Version Normalize(Version v)
            => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }
}
