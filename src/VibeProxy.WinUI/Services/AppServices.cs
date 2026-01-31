using VibeProxy.Core.Services;
using VibeProxy.WinUI.ViewModels;

namespace VibeProxy.WinUI.Services;

public sealed class AppServices
{
    public AppServices(SettingsStore settingsStore, AuthManager authManager, ServerManager serverManager,
        NotificationService notificationService, TrayService trayService, MainViewModel mainViewModel)
    {
        SettingsStore = settingsStore;
        AuthManager = authManager;
        ServerManager = serverManager;
        NotificationService = notificationService;
        TrayService = trayService;
        MainViewModel = mainViewModel;
    }

    public SettingsStore SettingsStore { get; }
    public AuthManager AuthManager { get; }
    public ServerManager ServerManager { get; }
    public NotificationService NotificationService { get; }
    public TrayService TrayService { get; }
    public MainViewModel MainViewModel { get; }
}