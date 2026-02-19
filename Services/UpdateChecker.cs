using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using upeko.Models;

namespace upeko.Services
{
    /// <summary>
    /// A singleton service that checks for bot updates using the GitHub API.
    /// </summary>
    public class UpdateChecker
    {
        #region Singleton Implementation

        private static UpdateChecker? _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the singleton instance of the UpdateChecker.
        /// </summary>
        public static UpdateChecker Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new UpdateChecker();
                    }
                }

                return _instance;
            }
        }

        #endregion

        #region Properties and Fields

        private readonly HttpClient _httpClient;
        private const string GitHubApiUrl = "https://api.github.com/repos/nadeko-bot/nadekobot/releases/latest";

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
        /// Event triggered during download progress.
        /// </summary>
        public event Action<double, string>? OnDownloadProgress;

        /// <summary>
        /// Event triggered when download is complete.
        /// </summary>
        public event Action<bool, string>? OnDownloadComplete;

        #endregion

        #region Events

        /// <summary>
        /// Triggered when a new version is found after checking for updates.
        /// </summary>
        public event Action<ReleaseModel>? OnNewVersionFound;

        #endregion

        private UpdateChecker()
        {
            // Private constructor to enforce singleton pattern
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Upeko-Bot-Updater");
        }

        /// <summary>
        /// Checks if a newer version is available for the specified current version.
        /// </summary>
        /// <param name="currentVersion">The current version of the bot.</param>
        /// <returns>True if a newer version is available, false otherwise.</returns>
        public bool IsUpdateAvailable(string? currentVersion)
        {
            if (currentVersion == null)
            {
                return true; // If no version is installed, an update is available
            }

            // Use the Version class to compare versions
            return CompareVersions(currentVersion, LatestVersion) < 0;
        }

        /// <summary>
        /// Compares two version strings using the built-in Version class.
        /// </summary>
        /// <param name="version1">First version string in X.Y.Z format.</param>
        /// <param name="version2">Second version string in X.Y.Z format.</param>
        private int CompareVersions(string version1, string version2)
        {
            // Parse the version strings into Version objects
            if (Version.TryParse(version1, out var v1) && Version.TryParse(version2, out var v2))
            {
                // Use the built-in comparison
                return v1.CompareTo(v2);
            }

            // If parsing fails, fall back to string comparison
            return string.Compare(version1, version2, StringComparison.Ordinal);
        }

        /// <summary>
        /// Checks for updates from GitHub API.
        /// </summary>
        /// <returns>Null if successful, error message if failed.</returns>
        public async Task<string?> CheckForUpdatesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(GitHubApiUrl);
                response.EnsureSuccessStatusCode();

                var newRelease = await response.Content.ReadFromJsonAsync(SourceJsonSerializer.Default.ReleaseModel);
                if (newRelease != null && (LatestRelease == null || LatestRelease.TagName != newRelease.TagName))
                {
                    LatestRelease = newRelease;
                    OnNewVersionFound?.Invoke(newRelease);
                }

                return null; // Success
            }
            catch (Exception ex)
            {
                return ex.ToString(); // Return error message
            }
        }

        /// <summary>
        /// Downloads and installs the bot.
        /// </summary>
        /// <param name="botName">The name of the bot.</param>
        /// <param name="botPath">The path where the bot should be installed.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task<string?> DownloadAndInstallBotAsync(string botName, string botPath)
        {
            if (string.IsNullOrWhiteSpace(botPath))
                throw new ArgumentNullException(nameof(botPath));

            try
            {
                // Make sure we have the latest release information
                if (LatestRelease == null)
                {
                    var error = await CheckForUpdatesAsync();
                    if (error != null)
                    {
                        return error;
                    }
                }

                if (LatestRelease?.Assets == null || LatestRelease.Assets.Length == 0)
                {
                    return "No download assets found in the release.";
                }

                // Determine the OS and architecture
                var os = PlatformSpecific.GetOS();
                var arch = PlatformSpecific.GetArchitecture();
                var extension = os == "win" ? ".zip" : ".tar.gz";

                // Find the appropriate asset
                var assetName = $"nadeko-{os}-{arch}{extension}";
                var asset = Array.Find(LatestRelease.Assets,
                    a => a.Name?.Equals(assetName, StringComparison.OrdinalIgnoreCase) == true);

                if (asset == null || string.IsNullOrEmpty(asset.DownloadUrl))
                {
                    return $"Could not find download for {assetName}.";
                }

                // Create a temporary directory for the download
                // this is done this way to avoid issues on windows
                // as Directory.Move doesn't work across drives, probably

                if (Path.GetFullPath(Path.Combine(botPath, "..")) == Path.GetFullPath(botPath))
                    throw new InvalidOperationException(
                        "What are you doing? Do not install the bot in the root folder. Change directory first." +
                        "If you delete the bot your system will be nuked.");

                var tempDir = Path.Combine(botPath, "..", ".dl-" + Guid.NewGuid());
                Directory.CreateDirectory(tempDir);

                // Download the file
                var downloadPath = Path.Combine(tempDir, assetName);
                OnDownloadProgress?.Invoke(0, $"Downloading {LatestRelease.TagName}...");

                using (var response =
                       await _httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength;
                    await using (var contentStream = await response.Content.ReadAsStreamAsync())
                    await using (var fileStream = new FileStream(downloadPath,
                                     FileMode.Create,
                                     FileAccess.Write,
                                     FileShare.None,
                                     8192,
                                     true))
                    {
                        var buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;

                            if (totalBytes.HasValue)
                            {
                                var progress = (double)totalBytesRead / totalBytes.Value;
                                OnDownloadProgress?.Invoke(progress,
                                    $"Downloading {LatestRelease.TagName}... {Math.Round(progress * 100)}%");
                            }
                        }
                    }
                }

                OnDownloadProgress?.Invoke(1, "Download complete. Extracting...");

                // Extract the downloaded file
                var extractPath = Path.Combine(tempDir, "extract");
                Directory.CreateDirectory(extractPath);

                if (extension == ".zip")
                {
                    ZipFile.ExtractToDirectory(downloadPath, extractPath);
                }
                else
                {
                    // For tar.gz, we need to use a process since .NET doesn't have built-in tar.gz extraction
                    using var process = new Process();
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "tar",
                        Arguments = $"-xzf \"{downloadPath}\" -C \"{extractPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    };

                    process.Start();
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        return $"Error extracting tar.gz: {error}";
                    }

                    // Make NadekoBot executable after downloading
                    var nadekoPath = Path.Combine(extractPath, "NadekoBot");

                    using var chmodProcess = new Process();
                    chmodProcess.StartInfo = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{nadekoPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    chmodProcess.Start();
                    await chmodProcess.WaitForExitAsync();
                }

                OnDownloadProgress?.Invoke(1, "Extraction complete. Installing...");


                var backupPath = Path.GetFullPath(Path.Combine(botPath, "..", $".old-{botName}"));

                // remove old backup
                if (Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                }

                // update - backup
                if (Directory.Exists(botPath))
                {
                    Directory.Move(botPath, backupPath);
                }
                else
                {
                    Directory.CreateDirectory(botPath);
                }

                // Move the extracted files to the installation directory
                Directory.Move(Path.Combine(extractPath, $"nadeko-{os}-{arch}"), botPath);

                // If there's a data directory in the old installation, copy it to the new one
                var oldDataPath = Path.Combine(backupPath, "data");
                var newDataPath = Path.Combine(botPath, "data");

                if (Directory.Exists(oldDataPath))
                {
                    CopyDirectory(oldDataPath, newDataPath);
                }

                // Clean up the temporary directory
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore errors during cleanup
                }

                OnDownloadComplete?.Invoke(true, LatestVersion);
                return null; // Success
            }
            catch (Exception ex)
            {
                OnDownloadComplete?.Invoke(false, ex.Message);
                return ex.ToString();
            }
        }


        /// <summary>
        /// Recursively copies a directory.
        /// </summary>
        private void CopyDirectory(string sourceDir, string destinationDir)
        {
            // Create the destination directory if it doesn't exist
            if (!Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            // Copy all files
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destinationDir, fileName);
                File.Copy(file, destFile, true);
            }

            // Recursively copy all subdirectories
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                var destDir = Path.Combine(destinationDir, dirName);
                CopyDirectory(dir, destDir);
            }
        }
    }
}