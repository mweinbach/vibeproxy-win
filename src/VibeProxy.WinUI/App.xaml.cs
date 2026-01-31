using System.Diagnostics;
using Microsoft.UI.Xaml;
using VibeProxy.Core.Services;
using VibeProxy.WinUI.Services;
using VibeProxy.WinUI.ViewModels;

namespace VibeProxy.WinUI;

public partial class App : Application
{
    private const string ReleasesUrl = "https://github.com/YOUR_GITHUB_USERNAME/vibeproxy-win/releases";

    private MainWindow? _window;

    public AppServices Services { get; }

    public App()
    {
        InitializeComponent();

        var settingsStore = new SettingsStore();
        var authManager = new AuthManager();
        var configManager = new ConfigManager();
        var proxyConfigWriter = new ProxyConfigWriter();
        var serverManager = new ServerManager(configManager, settingsStore, proxyConfigWriter);
        serverManager.SetResourceBasePath(AppContext.BaseDirectory);

        var notificationService = new NotificationService();
        var trayService = new TrayService();
        var mainViewModel = new MainViewModel(settingsStore, authManager, serverManager);

        Services = new AppServices(settingsStore, authManager, serverManager, notificationService, trayService, mainViewModel);

        settingsStore.VercelConfigChanged += () => serverManager.SyncProxyConfig();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(Services.MainViewModel);
        _window.AppWindow.Hide();

        Services.NotificationService.Register();
        Services.TrayService.Initialize();
        Services.TrayService.UpdateRunning(Services.ServerManager.IsRunning, Services.ServerManager.ProxyPort);

        Services.TrayService.OpenSettingsRequested += () => ShowMainWindow();
        Services.TrayService.ToggleServerRequested += async () =>
        {
            if (Services.ServerManager.IsRunning)
            {
                await Services.ServerManager.StopAsync();
                Services.NotificationService.Show("Server Stopped", "VibeProxy is now stopped");
            }
            else
            {
                var success = await Services.ServerManager.StartAsync();
                Services.NotificationService.Show(success ? "Server Started" : "Server Failed",
                    success ? "VibeProxy is now running" : "Could not start server");
            }
        };
        Services.TrayService.CopyUrlRequested += () => CopyServerUrl();
        Services.TrayService.CheckUpdatesRequested += () => OpenReleasesPage();
        Services.TrayService.QuitRequested += async () =>
        {
            if (Services.ServerManager.IsRunning)
            {
                await Services.ServerManager.StopAsync();
            }
            Exit();
        };

        Services.ServerManager.RunningChanged += running =>
        {
            Services.TrayService.UpdateRunning(running, Services.ServerManager.ProxyPort);
        };

        _ = Task.Run(async () =>
        {
            await Services.MainViewModel.InitializeAsync().ConfigureAwait(false);
            await Services.ServerManager.StartAsync().ConfigureAwait(false);
        });
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.ShowWindow();
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
}
