using System.IO;

namespace upeko;

public static class DiskSpace
{
    /// <summary>
    /// Free space available to the current user on the drive that holds <paramref name="path"/>.
    /// The path does not need to exist yet; its closest existing parent folder is used.
    /// </summary>
    public static long? GetAvailableBytes(string path)
    {
        try
        {
            var dir = Path.GetFullPath(path);
            while (!Directory.Exists(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                if (parent is null)
                    return null;
                dir = parent;
            }

            // On Linux and macOS DriveInfo queries the file system that contains this folder,
            // which is what we need when /home or the bots folder is a separate mount.
            return new DriveInfo(dir).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Bytes a NadekoBot download needs: the archive, its extracted copy, the old install kept as backup,
    /// plus some headroom.
    /// </summary>
    public static long RequiredForInstall(long archiveSize)
        => (archiveSize > 0 ? archiveSize : 100_000_000L) * 4 + 100_000_000L;

    public static string Format(long bytes)
        => bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F0} MB",
            _ => $"{bytes / 1024.0:F0} KB"
        };
}
