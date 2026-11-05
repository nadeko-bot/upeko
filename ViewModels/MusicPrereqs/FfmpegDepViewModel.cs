using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace upeko.ViewModels
{
    /// <summary>
    /// ffmpeg, which the bot uses to stream audio.
    /// </summary>
    public class FfmpegDepViewModel : DepViewModel
    {
        private const string WindowsVersion = "ffmpeg-6.1.1-essentials_build";
        private const string WindowsUrl =
            "https://github.com/GyanD/codexffmpeg/releases/download/6.1.1/" + WindowsVersion + ".zip";

        public FfmpegDepViewModel() : base("ffmpeg")
        {
        }

        protected override async Task<DepState> InternalCheckAsync()
            => await CanRunAsync("ffmpeg", TimeSpan.FromSeconds(10), "-version")
                ? DepState.Installed
                : DepState.NotInstalled;

        protected override async Task<bool> InternalInstallAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return await InstallWindowsAsync();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // The static builds below are Linux-only and there is no official macOS build to fetch.
                LastError = Loc["Toast_FfmpegMacBrew"];
                return false;
            }

            return await InstallLinuxAsync();
        }

        private static async Task<bool> InstallWindowsAsync()
        {
            var ffmpegPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "ffmpeg");
            var zipPath = Path.Combine(Path.GetTempPath(), $"upeko-ffmpeg-{Guid.NewGuid()}.zip");
            try
            {
                await DownloadToFileAsync(WindowsUrl, zipPath);

                if (Directory.Exists(ffmpegPath))
                    Directory.Delete(ffmpegPath, true);
                Directory.CreateDirectory(ffmpegPath);

                ZipFile.ExtractToDirectory(zipPath, ffmpegPath);
            }
            finally
            {
                File.Delete(zipPath);
            }

            PlatformSpecific.AddToUserPath(Path.Combine(ffmpegPath, WindowsVersion, "bin"));
            return true;
        }

        private static async Task<bool> InstallLinuxAsync()
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "amd64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
            };

            var staticUrl = $"https://www.johnvansickle.com/ffmpeg/old-releases/ffmpeg-6.0.1-{arch}-static.tar.xz";

            var targetDir = PlatformSpecific.UnixToolsDirectory;
            Directory.CreateDirectory(targetDir);

            await using var stream = await Http.GetStreamAsync(staticUrl);
            using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions());
            var found = 0;
            while (found < 2 && reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory)
                    continue;

                var fileName = Path.GetFileName(reader.Entry.Key);
                if (fileName is not ("ffmpeg" or "ffprobe"))
                    continue;

                var targetPath = Path.Combine(targetDir, fileName);
                reader.WriteEntryToFile(targetPath, new ExtractionOptions { Overwrite = true });
                PlatformSpecific.MakeExecutable(targetPath);
                found++;
            }

            return found == 2;
        }
    }
}
