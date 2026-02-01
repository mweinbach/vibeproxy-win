using System.Diagnostics;
using Microsoft.UI.Xaml;
using VibeProxy.Core.Services;
using VibeProxy.WinUI.Services;
using VibeProxy.WinUI.ViewModels;

namespace VibeProxy.WinUI;

public partial class App : Application
{
    private const string ReleasesUrl = "https://github.com/mweinbach/vibeproxy-win/releases";

    private MainWindow? _window;
    private int _lastServerLogCount;
    private CancellationTokenSource? _appCts;

    public AppServices Services { get; }

    public App()
    {
        InitializeComponent();

        LogService.Initialize();
        _appCts = new CancellationTokenSource();

        // Global exception handling
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Initialize services
        var settingsStore = new SettingsStore();
        var authManager = new AuthManager();
        var configManager = new ConfigManager();
        var proxyConfigWriter = new ProxyConfigWriter();
        var serverManager = new ServerManager(configManager, settingsStore, proxyConfigWriter);
        
        serverManager.SetResourceBasePath(AppContext.BaseDirectory);
        serverManager.LogUpdated += OnServerLogUpdated;

        // Listen for config changes that require server restart
        configManager.ConfigChanged += OnConfigChanged;

        var notificationService = new NotificationService();
        var trayService = new TrayService();
        var mainViewModel = new MainViewModel(settingsStore, authManager, serverManager);

        Services = new AppServices(settingsStore, authManager, serverManager, notificationService, trayService, mainViewModel);

        // Listen for Vercel config changes
        settingsStore.VercelConfigChanged += OnVercelConfigChanged;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        LogService.Write("App launched");

        try
        {
            _window = new MainWindow(Services.MainViewModel, Services.TrayService);
            _window.ShowWindow();
            LogService.Write("Window shown");
        }
        catch (Exception ex)
        {
            LogService.Write("Window show failed", ex);
        }

        try
        {
            Services.NotificationService.Register();
            Services.TrayService.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            Services.TrayService.UpdateRunning(Services.ServerManager.IsRunning, Services.ServerManager.ProxyPort);
            LogService.Write("Tray initialized");
        }
        catch (Exception ex)
        {
            LogService.Write("Tray init failed", ex);
        }

        // Setup tray event handlers
        SetupTrayEventHandlers();

        // Setup server state change handler
        Services.ServerManager.RunningChanged += OnServerRunningChanged;

        // Background initialization and startup
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_appCts!.Token);
                cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second startup timeout

                await Services.MainViewModel.InitializeAsync(cts.Token).ConfigureAwait(false);
                LogService.Write("MainViewModel initialized");

                var success = await Services.ServerManager.StartAsync(cts.Token).ConfigureAwait(false);
                LogService.Write($"Server start result: {success}");

                if (!success)
                {
                    Services.NotificationService.Show("Server Failed", "Could not start VibeProxy server. Check logs for details.");
                }
            }
            catch (OperationCanceledException)
            {
                LogService.Write("Background startup cancelled or timed out");
            }
            catch (Exception ex)
            {
                LogService.Write("Background startup failed", ex);
            }
        });
    }

    private void SetupTrayEventHandlers()
    {
        Services.TrayService.OpenSettingsRequested += ShowMainWindow;
        
        Services.TrayService.ToggleServerRequested += async () =>
        {
            try
            {
                if (Services.ServerManager.IsRunning)
                {
                    await Services.ServerManager.StopAsync();
                    Services.NotificationService.Show("Server Stopped", "VibeProxy is now stopped");
                }
                else
                {
                    var success = await Services.ServerManager.StartAsync();
                    Services.NotificationService.Show(
                        success ? "Server Started" : "Server Failed",
                        success ? "VibeProxy is now running" : "Could not start server");
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Toggle server failed", ex);
                Services.NotificationService.Show("Error", "Failed to toggle server");
            }
        };

        Services.TrayService.CopyUrlRequested += () =>
        {
            try
            {
                CopyServerUrl();
            }
            catch (Exception ex)
            {
                LogService.Write("Copy URL failed", ex);
            }
        };

        Services.TrayService.CheckUpdatesRequested += OpenReleasesPage;
        
        Services.TrayService.QuitRequested += async () =>
        {
            try
            {
                if (Services.ServerManager.IsRunning)
                {
                    await Services.ServerManager.StopAsync();
                }
                Cleanup();
                Exit();
            }
            catch (Exception ex)
            {
                LogService.Write("Quit failed", ex);
                Exit();
            }
        };
    }

    private void ShowMainWindow()
    {
        _window?.ShowWindow();
    }

    private void CopyServerUrl()
    {
        var url = $"http://localhost:{Services.ServerManager.ProxyPort}";
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(url);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        Services.NotificationService.Show("Copied", "Server URL copied to clipboard");
    }

    private void OpenReleasesPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ReleasesUrl,
            UseShellExecute = true
        });
    }

    private void OnServerRunningChanged(bool running)
    {
        Services.TrayService.UpdateRunning(running, Services.ServerManager.ProxyPort);
    }

    private void OnServerLogUpdated(IReadOnlyList<string> logs)
    {
        if (logs.Count <= _lastServerLogCount)
        {
            return;
        }

        for (var i = _lastServerLogCount; i < logs.Count; i++)
        {
            LogService.Write($"[Server] {logs[i]}");
        }

        _lastServerLogCount = logs.Count;
    }

    private void OnVercelConfigChanged()
    {
        try
        {
            Services.ServerManager.SyncProxyConfig();
        }
        catch (Exception ex)
        {
            LogService.Write("Failed to sync proxy config", ex);
        }
    }

    private async void OnConfigChanged()
    {
        try
        {
            LogService.Write("Configuration changed, restarting server...");
            
            if (Services.ServerManager.IsRunning)
            {
                await Services.ServerManager.StopAsync();
            }
            
            var success = await Services.ServerManager.StartAsync();
            
            if (success)
            {
                Services.NotificationService.Show("Config Updated", "Server restarted with new configuration");
            }
            else
            {
                Services.NotificationService.Show("Config Error", "Failed to restart server with new configuration");
            }
        }
        catch (Exception ex)
        {
            LogService.Write("Config change handling failed", ex);
            Services.NotificationService.Show("Error", "Failed to apply configuration changes");
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        LogService.Write("WinUI unhandled exception", args.Exception);
        args.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        LogService.Write("Unhandled exception", args.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        LogService.Write("Unobserved task exception", args.Exception);
        args.SetObserved();
    }

    private void Cleanup()
    {
        try
        {
            _appCts?.Cancel();
            _appCts?.Dispose();
            _appCts = null;

            Services.MainViewModel?.Dispose();
            Services.ServerManager?.Dispose();
            Services.TrayService?.Dispose();
        }
        catch (Exception ex)
        {
            LogService.Write("Cleanup failed", ex);
        }
    }
}
