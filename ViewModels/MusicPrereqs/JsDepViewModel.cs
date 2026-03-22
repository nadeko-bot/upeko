using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace upeko.ViewModels
{
    public class JsDepViewModel : DepViewModel
    {
        public JsDepViewModel() : base("js")
        {
        }

        protected override async Task<DepState> InternalCheckAsync()
        {
            await Task.Yield();

            string[] runtimes = ["deno", "bun", "node"];
            foreach (var runtime in runtimes)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = runtime,
                        Arguments = "--version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var p = Process.Start(psi);
                    try { p?.Kill(); } catch { }
                    return DepState.Installed;
                }
                catch
                {
                }
            }

            return DepState.NotInstalled;
        }

        protected override async Task<bool> InternalInstallAsync()
        {
            try
            {
                var arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "x86_64",
                    Architecture.Arm64 => "aarch64",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
                };

                string target;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    target = $"{arch}-pc-windows-msvc";
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    target = $"{arch}-apple-darwin";
                else
                    target = $"{arch}-unknown-linux-gnu";

                var zipUrl = $"https://github.com/denoland/deno/releases/latest/download/deno-{target}.zip";
                var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "deno.exe" : "deno";

                using var http = new HttpClient();
                var zipBytes = await http.GetByteArrayAsync(zipUrl);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var denoDir = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "deno");
                    if (Directory.Exists(denoDir))
                        Directory.Delete(denoDir, true);
                    Directory.CreateDirectory(denoDir);

                    var zipPath = Path.Combine(denoDir, "deno.zip");
                    await File.WriteAllBytesAsync(zipPath, zipBytes);
                    ZipFile.ExtractToDirectory(zipPath, denoDir);
                    File.Delete(zipPath);

                    try
                    {
                        Environment.SetEnvironmentVariable("path",
                            Environment.GetEnvironmentVariable("path", EnvironmentVariableTarget.User) + ";" + denoDir,
                            EnvironmentVariableTarget.User);
                    }
                    catch { }
                }
                else
                {
                    var targetDir = Path.GetFullPath(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "sbin"));
                    Directory.CreateDirectory(targetDir);

                    var zipPath = Path.Combine(targetDir, "deno.zip");
                    await File.WriteAllBytesAsync(zipPath, zipBytes);
                    ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
                    File.Delete(zipPath);

                    var denoPath = Path.Combine(targetDir, exeName);
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{denoPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        }
                    };
                    process.Start();
                    await process.WaitForExitAsync();
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
