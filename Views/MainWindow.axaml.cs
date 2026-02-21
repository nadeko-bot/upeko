using System;
using Avalonia;
using Avalonia.Controls;
using Serilog;
using upeko.Services;
using upeko.ViewModels;

namespace upeko.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.CloseRequested = () =>
            {
                _forceClose = true;
                Close();
            };
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_forceClose)
        {
            base.OnClosing(e);
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            if (vm.MinimizeToTray)
            {
                e.Cancel = true;
                Log.Information("Minimized to system tray");
                Hide();
                if (Application.Current is App app)
                    app.SendSystemNotification(
                        LocalizationService.Instance["Tray_Tooltip"],
                        LocalizationService.Instance["Tray_MinimizedNotification"]);
                return;
            }

            if (!vm.RequestExit())
            {
                e.Cancel = true;
                return;
            }
        }

        base.OnClosing(e);
    }
}