using System.Drawing;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace VibeProxy.WinUI.Services;

public sealed class TrayService
{
    private readonly string _activeIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "icon-active.ico");
    private readonly string _inactiveIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "icon-inactive.ico");
    private Icon? _activeIcon;
    private Icon? _inactiveIcon;

    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
    private TaskbarIcon? _trayIcon;
    private MenuFlyoutItem? _statusItem;
    private MenuFlyoutItem? _startStopItem;
    private MenuFlyoutItem? _copyUrlItem;

    public event Action? OpenSettingsRequested;
    public event Action? ToggleServerRequested;
    public event Action? CopyUrlRequested;
    public event Action? CheckUpdatesRequested;
    public event Action? QuitRequested;

    public void Initialize(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _activeIcon = LoadIcon(_activeIconPath);
        _inactiveIcon = LoadIcon(_inactiveIconPath);
        _trayIcon = new TaskbarIcon
        {
            Icon = _inactiveIcon,
            ToolTipText = "VibeProxy"
        };

        var menu = new MenuFlyout();
        _statusItem = new MenuFlyoutItem { Text = "Server: Stopped" };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        var openSettings = new MenuFlyoutItem { Text = "Open Settings" };
        openSettings.Click += (_, _) => OpenSettingsRequested?.Invoke();
        menu.Items.Add(openSettings);
        menu.Items.Add(new MenuFlyoutSeparator());

        _startStopItem = new MenuFlyoutItem { Text = "Start Server" };
        _startStopItem.Click += (_, _) => ToggleServerRequested?.Invoke();
        menu.Items.Add(_startStopItem);

        _copyUrlItem = new MenuFlyoutItem { Text = "Copy Server URL", IsEnabled = false };
        _copyUrlItem.Click += (_, _) => CopyUrlRequested?.Invoke();
        menu.Items.Add(_copyUrlItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        var checkUpdates = new MenuFlyoutItem { Text = "Check for Updates..." };
        checkUpdates.Click += (_, _) => CheckUpdatesRequested?.Invoke();
        menu.Items.Add(checkUpdates);
        menu.Items.Add(new MenuFlyoutSeparator());

        var quit = new MenuFlyoutItem { Text = "Quit" };
        quit.Click += (_, _) => QuitRequested?.Invoke();
        menu.Items.Add(quit);

        _trayIcon.ContextFlyout = menu;
        _trayIcon.ForceCreate();
    }

    public void UpdateRunning(bool isRunning, int port)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(() => UpdateRunning(isRunning, port));
            return;
        }

        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Icon = isRunning ? _activeIcon : _inactiveIcon;

        if (_statusItem is not null)
        {
            _statusItem.Text = isRunning ? $"Server: Running (port {port})" : "Server: Stopped";
        }

        if (_startStopItem is not null)
        {
            _startStopItem.Text = isRunning ? "Stop Server" : "Start Server";
        }

        if (_copyUrlItem is not null)
        {
            _copyUrlItem.IsEnabled = isRunning;
        }
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
    }

    private static Icon? LoadIcon(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Icon(path);
        }
        catch
        {
            return null;
        }
    }
}
