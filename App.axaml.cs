using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using upeko.Services;
using upeko.ViewModels;
using upeko.Views;

namespace upeko;

public partial class App : Application
{
    public static Window MainWindow { get; private set; } = null!;
    public static MainWindowViewModel? ViewModel { get; private set; }

    private TrayIcon? _trayIcon;
    private DateTime _lastNotification = DateTime.MinValue;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            LocalizationService.Instance.SetLanguage("en-US");

            ViewModel = new MainWindowViewModel();

            var config = ViewModel.GetConfig();
            if (config.Language != "en-US")
                LocalizationService.Instance.SetLanguage(config.Language);

            MainWindow = new MainWindow
            {
                DataContext = ViewModel,
            };
            desktop.MainWindow = MainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            SetupTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        var showItem = new NativeMenuItem("Show Upeko");
        showItem.Click += (_, _) => ShowMainWindow();

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Upeko",
            Menu = menu,
            IsVisible = true
        };

        try
        {
            _trayIcon.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://upeko/Assets/upeko.ico")));
        }
        catch
        {
            // Icon may not load in all environments
        }

        _trayIcon.Clicked += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    public void ExitApplication()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (MainWindow is MainWindow mw)
            mw.ForceClose();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    public void SendSystemNotification(string title, string message)
    {
        if (DateTime.UtcNow - _lastNotification < TimeSpan.FromMinutes(5))
            return;

        _lastNotification = DateTime.UtcNow;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo("notify-send", $"\"{title}\" \"{message}\"")
                    { UseShellExecute = false, CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("osascript",
                    $"-e 'display notification \"{message}\" with title \"{title}\"'")
                    { UseShellExecute = false, CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var script = "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
                             "$xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                             "$text = $xml.GetElementsByTagName('text'); " +
                             $"$text[0].AppendChild($xml.CreateTextNode('{title}')) | Out-Null; " +
                             $"$text[1].AppendChild($xml.CreateTextNode('{message}')) | Out-Null; " +
                             "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Upeko').Show([Windows.UI.Notifications.ToastNotification]::new($xml))";
                Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -Command \"{script}\"")
                    { UseShellExecute = false, CreateNoWindow = true });
            }
        }
        catch
        {
            // best-effort
        }
    }

#pragma warning disable IL2026
    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
#pragma warning restore IL2026
}