using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Serilog;
using upeko.Services;
using upeko.ViewModels;

namespace upeko.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;
    private bool _isClosed;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void ForceClose()
    {
        if (_isClosed)
            return;

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
                if (Application.Current is App app)
                    app.Quit();
                else
                    ForceClose();
            };
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape closes whichever dialog is open.
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel vm)
        {
            if (vm.Changelog.IsOpen)
            {
                vm.Changelog.CloseCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.IsDeleteModalOpen)
            {
                vm.CancelDeleteCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.IsExitModalOpen)
            {
                vm.CancelExitCommand.Execute(null);
                e.Handled = true;
            }
        }

        base.OnKeyDown(e);
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

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        base.OnClosed(e);

        // With minimize-to-tray off, closing the window means quitting.
        if (Application.Current is App app)
            app.Quit();
    }
}
