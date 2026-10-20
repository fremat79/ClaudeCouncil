using System;
using System.Windows;
using System.Windows.Threading;
using ClaudeCouncilManager.Services;
using ClaudeCouncilManager.ViewModels;
using ClaudeCouncilManager.Views;
using WinForms = System.Windows.Forms;

namespace ClaudeCouncilManager;

public partial class App : Application
{
    private const string AppTitle = "Claude Council Manager";

    private MainWindow? _window;
    private WinForms.NotifyIcon? _trayIcon;
    private WindowState _restoreState = WindowState.Normal;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        ThemeManager.Initialize();

        var vm = new MainViewModel();
        _window = new MainWindow { DataContext = vm };
        _window.Closing += (_, e) =>
        {
            // Markdown viewers with unsaved edits get their say first; keeping one open cancels the exit.
            if (!MarkdownWindow.CloseAll())
            {
                e.Cancel = true;
                return;
            }
            vm.SaveNow();
        };
        // Minimizing sends the window to the notification area; closing still quits the app.
        _window.StateChanged += OnWindowStateChanged;
        CreateTrayIcon();
        _window.Show();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window is null) return;
        if (_window.WindowState == WindowState.Minimized)
            HideToTray();
        else
            _restoreState = _window.WindowState; // so a maximized window comes back maximized
    }

    /// <summary>The tray icon is shown only while the window is hidden there.</summary>
    private void CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _window?.Close());

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = AppTitle,
            ContextMenuStrip = menu,
            Visible = false,
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) ShowFromTray();
        };
    }

    private void HideToTray()
    {
        if (_window is null || _trayIcon is null) return;
        _window.Hide();
        _trayIcon.Visible = true;
        if (_trayHintShown) return;
        _trayHintShown = true;
        _trayIcon.ShowBalloonTip(3000, AppTitle,
            "The app is in the notification area: click its icon to bring it back.", WinForms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = _restoreState;
        _window.Activate();
        if (_trayIcon is not null) _trayIcon.Visible = false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        base.OnExit(e);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/council.ico"));
            if (resource is not null)
                return new System.Drawing.Icon(resource.Stream, WinForms.SystemInformation.SmallIconSize);
        }
        catch
        {
            // Fall back to a stock icon if the resource can't be loaded.
        }
        return System.Drawing.SystemIcons.Application;
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
