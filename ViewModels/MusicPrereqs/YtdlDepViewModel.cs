using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace upeko.ViewModels
{
    /// <summary>
    /// yt-dlp, which the bot uses to play music from YouTube and other sites.
    /// </summary>
    public class YtdlDepViewModel : DepViewModel
    {
        private const string BaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";

        public YtdlDepViewModel() : base("yt-dlp")
        {
        }

        // "-U" also keeps an existing yt-dlp up to date, which matters because sites change often.
        protected override async Task<DepState> InternalCheckAsync()
            => await CanRunAsync("yt-dlp", TimeSpan.FromSeconds(60), "-U")
                ? DepState.Installed
                : DepState.NotInstalled;

        protected override async Task<bool> InternalInstallAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var ytdlPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "yt-dlp");
                if (Directory.Exists(ytdlPath))
                    Directory.Delete(ytdlPath, true);
                Directory.CreateDirectory(ytdlPath);

                await DownloadToFileAsync(BaseUrl + "yt-dlp.exe", Path.Combine(ytdlPath, "yt-dlp.exe"));
                PlatformSpecific.AddToUserPath(ytdlPath);
                return true;
            }

            // The standalone builds do not need Python, unlike the plain "yt-dlp" zipapp.
            var asset = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "yt-dlp_macos"
                : RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? "yt-dlp_linux_aarch64"
                    : "yt-dlp_linux";

            var targetDir = PlatformSpecific.UnixToolsDirectory;
            Directory.CreateDirectory(targetDir);

            var ytdlpPath = Path.Combine(targetDir, "yt-dlp");
            var tempPath = ytdlpPath + ".download";
            await DownloadToFileAsync(BaseUrl + asset, tempPath);
            File.Move(tempPath, ytdlpPath, overwrite: true);
            PlatformSpecific.MakeExecutable(ytdlpPath);
            return true;
        }
    }
}
