using System;
using System.IO;
using Avalonia;
using Serilog;
using upeko.Services;

namespace upeko;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Before the logger: a second Upeko must not open the log file of the first one.
        using var instance = SingleInstance.Claim(App.ShowFromAnotherInstance);
        if (instance is null)
            return;

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "upeko", "logs", "upeko-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 5_242_880,
                retainedFileCountLimit: 2,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Upeko starting");
            if (instance.Problem is not null)
                Log.Warning("Running without the single instance check. {Problem}", instance.Problem);

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Upeko terminated unexpectedly");
        }
        finally
        {
            Log.Information("Upeko shutting down");
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
