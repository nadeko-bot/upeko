using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace upeko.Services;

/// <summary>
/// Lets only one Upeko run for each user. The first Upeko holds a lock file and listens on a named pipe.
/// A second Upeko asks the first one to show its window and exits, so it cannot start the same bots again.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string ShowMessage = "show";

    private readonly FileStream? _lockFile;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Why this Upeko runs without the check, when the lock file could not be used.
    /// </summary>
    public string? Problem { get; }

    private SingleInstance(FileStream? lockFile, string pipeName, string? problem)
    {
        _lockFile = lockFile;
        _pipeName = pipeName;
        Problem = problem;
    }

    public static string DefaultFolder
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "upeko");

    /// <summary>
    /// Returns null when another Upeko runs; it was asked to show its window. Otherwise this Upeko continues,
    /// and <paramref name="onShowRequested"/> runs on a background thread each time another Upeko starts.
    /// </summary>
    public static SingleInstance? Claim(Action onShowRequested, string? folder = null)
    {
        folder ??= DefaultFolder;
        var lockPath = Path.Combine(folder, "upeko.lock");
        var pipeName = PipeNameFor(lockPath);

        FileStream lockFile;
        try
        {
            Directory.CreateDirectory(folder);
            // On Linux and macOS, .NET takes an flock for FileShare.None, so the lock works between processes there too.
            lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            // The lock is taken. When no Upeko answers on the pipe, the lock failed for another reason,
            // and this Upeko runs rather than not start at all.
            if (TryAskRunningInstanceToShow(pipeName))
                return null;

            return new SingleInstance(null, pipeName, $"Lock file {lockPath} is in use, but no Upeko answered: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new SingleInstance(null, pipeName, $"Could not create lock file {lockPath}: {ex.Message}");
        }

        var instance = new SingleInstance(lockFile, pipeName, null);
        instance.Listen(onShowRequested);
        return instance;
    }

    private void Listen(Action onShowRequested)
    {
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(ct);

                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var message = await reader.ReadLineAsync(readTimeout.Token);
                    if (message?.Trim() == ShowMessage)
                    {
                        Log.Information("Another Upeko was started; showing this window instead");
                        onShowRequested();
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Single instance pipe failed");
                    try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }, ct);
    }

    private static bool TryAskRunningInstanceToShow(string pipeName)
    {
        try
        {
            // Windows gives the foreground only to the process that the user started; pass it on.
            if (OperatingSystem.IsWindows())
                AllowSetForegroundWindow(-1);

            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            client.Write(Encoding.UTF8.GetBytes(ShowMessage + "\n"));
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// One pipe for each lock file, so two users on one machine do not share a pipe.
    /// </summary>
    private static string PipeNameFor(string lockPath)
    {
        // FNV-1a: string.GetHashCode differs between processes.
        var hash = 2166136261u;
        foreach (var c in lockPath)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return $"upeko-{hash:x8}";
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    public void Dispose()
    {
        _cts.Cancel();
        _lockFile?.Dispose();
    }
}
