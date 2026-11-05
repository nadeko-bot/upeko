using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
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
    private NativeMenuItem? _showItem;
    private NativeMenuItem? _exitItem;
    private DateTime _lastNotification = DateTime.MinValue;
    private bool _quitting;

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

            // The window hides to the tray instead of closing, so only Quit() ends the app.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            SetupTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        _showItem = new NativeMenuItem();
        _showItem.Click += (_, _) => ShowMainWindow();

        _exitItem = new NativeMenuItem();
        _exitItem.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        menu.Items.Add(_showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_exitItem);

        _trayIcon = new TrayIcon
        {
            Menu = menu,
            IsVisible = true
        };

        UpdateTrayText();
        LocalizationService.Instance.LanguageChanged += UpdateTrayText;

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

    private void UpdateTrayText()
    {
        var loc = LocalizationService.Instance;
        if (_showItem is not null) _showItem.Header = loc["Tray_Show"];
        if (_exitItem is not null) _exitItem.Header = loc["Tray_Exit"];
        if (_trayIcon is not null) _trayIcon.ToolTipText = loc["Tray_Tooltip"];
    }

    private void ShowMainWindow()
    {
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    /// <summary>
    /// Exit from the tray menu. Asks first when bots are running.
    /// </summary>
    public void ExitApplication()
    {
        if (ViewModel is not null && !ViewModel.RequestExit())
        {
            ShowMainWindow();
            return;
        }

        Quit();
    }

    /// <summary>
    /// Ends the app without asking.
    /// </summary>
    public void Quit()
    {
        if (_quitting)
            return;
        _quitting = true;

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
            ProcessStartInfo psi;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                psi = new ProcessStartInfo("notify-send");
                psi.ArgumentList.Add(title);
                psi.ArgumentList.Add(message);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                static string AppleScriptString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                psi = new ProcessStartInfo("osascript");
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add($"display notification {AppleScriptString(message)} with title {AppleScriptString(title)}");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                static string PsString(string s) => "'" + s.Replace("'", "''") + "'";
                var script = "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
                             "$xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                             "$text = $xml.GetElementsByTagName('text'); " +
                             $"$text[0].AppendChild($xml.CreateTextNode({PsString(title)})) | Out-Null; " +
                             $"$text[1].AppendChild($xml.CreateTextNode({PsString(message)})) | Out-Null; " +
                             "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Upeko').Show([Windows.UI.Notifications.ToastNotification]::new($xml))";
                psi = new ProcessStartInfo("powershell");
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(script);
            }
            else
            {
                return;
            }

            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process.Start(psi)?.Dispose();
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
