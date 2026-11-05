using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace upeko.ViewModels
{
    /// <summary>
    /// A JavaScript runtime, which yt-dlp needs for YouTube. Any of deno, bun or node counts;
    /// upeko installs deno.
    /// </summary>
    public class JsDepViewModel : DepViewModel
    {
        public JsDepViewModel() : base("js")
        {
        }

        protected override async Task<DepState> InternalCheckAsync()
        {
            foreach (var runtime in new[] { "deno", "bun", "node" })
            {
                if (await CanRunAsync(runtime, TimeSpan.FromSeconds(10), "--version"))
                    return DepState.Installed;
            }

            return DepState.NotInstalled;
        }

        protected override async Task<bool> InternalInstallAsync()
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => "aarch64",
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
            };

            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            string target;
            if (isWindows)
                target = $"{arch}-pc-windows-msvc";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                target = $"{arch}-apple-darwin";
            else
                target = $"{arch}-unknown-linux-gnu";

            var zipUrl = $"https://github.com/denoland/deno/releases/latest/download/deno-{target}.zip";

            string targetDir;
            if (isWindows)
            {
                targetDir = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "deno");
                if (Directory.Exists(targetDir))
                    Directory.Delete(targetDir, true);
            }
            else
            {
                targetDir = PlatformSpecific.UnixToolsDirectory;
            }

            Directory.CreateDirectory(targetDir);

            var zipPath = Path.Combine(Path.GetTempPath(), $"upeko-deno-{Guid.NewGuid()}.zip");
            try
            {
                await DownloadToFileAsync(zipUrl, zipPath);
                ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
            }
            finally
            {
                File.Delete(zipPath);
            }

            if (isWindows)
                PlatformSpecific.AddToUserPath(targetDir);
            else
                PlatformSpecific.MakeExecutable(Path.Combine(targetDir, "deno"));

            return true;
        }
    }
}
