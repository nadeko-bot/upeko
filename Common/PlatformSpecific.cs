using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace upeko;

public static class PlatformSpecific
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string GetExecutableName()
        => IsWindows ? "NadekoBot.exe" : "NadekoBot";

    /// <summary>
    /// Gets the OS identifier for the download URL.
    /// </summary>
    public static string GetOS()
    {
        if (IsWindows)
            return "win";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "osx";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux";

        throw new PlatformNotSupportedException("Unsupported OS platform");
    }

    /// <summary>
    /// Gets the architecture identifier for the download URL.
    /// </summary>
    public static string GetArchitecture()
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                $"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
        };

    /// <summary>
    /// Opens a folder, file or URL with the system's default handler
    /// (Explorer, Finder or xdg-open).
    /// </summary>
    public static void Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>
    /// Where upeko installs ffmpeg, yt-dlp and deno on Linux and macOS.
    /// </summary>
    public static string UnixToolsDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

    /// <summary>
    /// The PATH upeko uses to look for tools and to start bots. On Linux and macOS it puts
    /// ~/.local/bin (and ~/.local/sbin, where older upeko versions installed tools) first,
    /// because desktop sessions often start apps without them on PATH.
    /// </summary>
    public static string GetToolSearchPath()
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (IsWindows)
            return current;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var extra = new[] { UnixToolsDirectory, Path.Combine(home, ".local", "sbin") };
        var parts = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Path.PathSeparator, extra.Concat(parts).Distinct());
    }

    /// <summary>
    /// Creates a ProcessStartInfo for a tool that may live in one of upeko's tool directories.
    /// </summary>
    public static ProcessStartInfo ToolStartInfo(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveTool(fileName),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["PATH"] = GetToolSearchPath();
        return psi;
    }

    private static string ResolveTool(string fileName)
    {
        if (IsWindows || fileName.Contains(Path.DirectorySeparatorChar))
            return fileName;

        foreach (var dir in GetToolSearchPath().Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return fileName;
    }

    /// <summary>
    /// Adds a folder to the user's PATH (Windows only) and to this process's PATH,
    /// so bots started from this session find the tool without restarting upeko.
    /// </summary>
    public static void AddToUserPath(string folder)
    {
        if (!IsWindows)
            return;

        static IEnumerable<string> Split(string? path)
            => (path ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries);

        static bool Contains(string? path, string folder)
            => Split(path).Any(p => string.Equals(p.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        if (!Contains(userPath, folder))
            Environment.SetEnvironmentVariable("PATH", string.Join(';', Split(userPath).Append(folder)),
                EnvironmentVariableTarget.User);

        var processPath = Environment.GetEnvironmentVariable("PATH");
        if (!Contains(processPath, folder))
            Environment.SetEnvironmentVariable("PATH", string.Join(';', Split(processPath).Append(folder)));
    }

    public static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(path, File.GetUnixFileMode(path)
                                   | UnixFileMode.UserExecute
                                   | UnixFileMode.GroupExecute
                                   | UnixFileMode.OtherExecute);
    }
}
