using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace VibeProxy.WinUI.Services;

public sealed class TrayService
{
    private readonly Uri _activeIcon = new("ms-appx:///Assets/Icons/icon-active.png");
    private readonly Uri _inactiveIcon = new("ms-appx:///Assets/Icons/icon-inactive.png");

    private TaskbarIcon? _trayIcon;
    private MenuFlyoutItem? _statusItem;
    private MenuFlyoutItem? _startStopItem;
    private MenuFlyoutItem? _copyUrlItem;

    public event Action? OpenSettingsRequested;
    public event Action? ToggleServerRequested;
    public event Action? CopyUrlRequested;
    public event Action? CheckUpdatesRequested;
    public event Action? QuitRequested;

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(_inactiveIcon),
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
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.IconSource = new BitmapImage(isRunning ? _activeIcon : _inactiveIcon);

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
}
