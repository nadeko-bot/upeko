using System.Runtime.InteropServices;

namespace upeko;

public static class DiskSpace
{
    public static long? GetAvailableBytes(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (GetDiskFreeSpaceEx(path, out var freeBytesAvailable, out _, out _))
                    return (long)freeBytesAvailable;
                return null;
            }
            else
            {
                var buf = new Statvfs();
                if (statvfs(path, ref buf) == 0)
                    return (long)(buf.f_bavail * buf.f_frsize);
                return null;
            }
        }
        catch { return null; }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int statvfs(string path, ref Statvfs buf);

    [StructLayout(LayoutKind.Sequential)]
    private struct Statvfs
    {
        public ulong f_bsize;
        public ulong f_frsize;
        public ulong f_blocks;
        public ulong f_bfree;
        public ulong f_bavail;
        public ulong f_files;
        public ulong f_ffree;
        public ulong f_favail;
        public ulong f_fsid;
        public ulong f_flag;
        public ulong f_namemax;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);
}
