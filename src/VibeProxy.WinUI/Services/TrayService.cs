using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace VibeProxy.WinUI.Services;

public sealed class TrayService
{
    private readonly Uri _activeIconLight = new("ms-appx:///Assets/Icons/icon-active.ico");
    private readonly Uri _inactiveIconLight = new("ms-appx:///Assets/Icons/icon-inactive.ico");
    private readonly Uri _activeIconDark = new("ms-appx:///Assets/Icons/icon-active-dark.ico");
    private readonly Uri _inactiveIconDark = new("ms-appx:///Assets/Icons/icon-inactive-dark.ico");

    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
    private TaskbarIcon? _trayIcon;
    private BitmapImage? _activeIconImage;
    private BitmapImage? _inactiveIconImage;
    private bool _isRunning;
    private bool? _useLightIcons;
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
        UpdateTheme(ElementTheme.Default);
        _trayIcon = new TaskbarIcon
        {
            IconSource = _inactiveIconImage ?? new BitmapImage(_inactiveIconLight),
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

        _isRunning = isRunning;

        if (_trayIcon is null || _trayIcon.IsDisposed)
        {
            return;
        }

        if (_activeIconImage is null || _inactiveIconImage is null)
        {
            UpdateTheme(ElementTheme.Default);
        }

        var nextIcon = isRunning ? _activeIconImage : _inactiveIconImage;
        if (nextIcon is not null)
        {
            try
            {
                _trayIcon.IconSource = nextIcon;
            }
            catch (Exception ex)
            {
                LogService.Write("Tray icon update failed", ex);
            }
        }

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

    public void UpdateTheme(ElementTheme theme)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(() => UpdateTheme(theme));
            return;
        }

        var useLightIcons = theme switch
        {
            ElementTheme.Dark => true,
            ElementTheme.Light => false,
            _ => Application.Current?.RequestedTheme == ApplicationTheme.Dark
        };

        if (_useLightIcons.HasValue
            && _useLightIcons.Value == useLightIcons
            && _activeIconImage is not null
            && _inactiveIconImage is not null)
        {
            return;
        }

        _useLightIcons = useLightIcons;

        var activeUri = useLightIcons ? _activeIconLight : _activeIconDark;
        var inactiveUri = useLightIcons ? _inactiveIconLight : _inactiveIconDark;
        _activeIconImage = new BitmapImage(activeUri);
        _inactiveIconImage = new BitmapImage(inactiveUri);

        if (_trayIcon is null || _trayIcon.IsDisposed)
        {
            return;
        }

        _trayIcon.IconSource = _isRunning ? _activeIconImage : _inactiveIconImage;
    }

}
