namespace upeko.Models;

public record DownloadProgressInfo(
    double Progress,
    double SpeedBytesPerSec,
    long BytesDownloaded,
    long? TotalBytes,
    double? EtaSeconds,
    string Status);
