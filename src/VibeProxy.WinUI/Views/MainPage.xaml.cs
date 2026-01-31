using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VibeProxy.Core.Models;
using VibeProxy.WinUI.ViewModels;

namespace VibeProxy.WinUI.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;

    public MainPage()
    {
        InitializeComponent();
    }

    public void Initialize(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        ViewModel.ConnectFlowRequested += HandleConnectAsync;
        ViewModel.RemoveFlowRequested += HandleRemoveAsync;
        Loaded += OnLoaded;
        
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag.ToString();
            DashboardSection.Visibility = tag == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
            ServicesSection.Visibility = tag == "Services" ? Visibility.Visible : Visibility.Collapsed;
            SettingsSection.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
            AboutSection.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
            
            ContentScrollViewer.ScrollToVerticalOffset(0);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await UpdateLaunchToggleAsync();
    }

    private async Task UpdateLaunchToggleAsync()
    {
        try
        {
            var startupTask = await Windows.ApplicationModel.StartupTask.GetAsync("VibeProxyStartup");
            LaunchToggle.IsOn = startupTask.State == Windows.ApplicationModel.StartupTaskState.Enabled;
        }
        catch
        {
            LaunchToggle.IsOn = false;
        }
    }

    private async void OnLaunchAtLoginToggled(object sender, RoutedEventArgs e)
    {
        try
        {
            var startupTask = await Windows.ApplicationModel.StartupTask.GetAsync("VibeProxyStartup");
            if (LaunchToggle.IsOn)
            {
                await startupTask.RequestEnableAsync();
            }
            else
            {
                startupTask.Disable();
            }
        }
        catch
        {
            // Ignore
        }
    }

    private void OnOpenAuthFolderClicked(object sender, RoutedEventArgs e)
    {
        var authDir = ((App)Application.Current).Services.AuthManager.AuthDirectory;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = authDir,
            UseShellExecute = true
        });
    }

    private void OnCopyUrlClicked(object sender, RoutedEventArgs e)
    {
        var services = ((App)Application.Current).Services;
        var url = $"http://localhost:{services.ServerManager.ProxyPort}";
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(url);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        services.NotificationService.Show("Copied", "Server URL copied to clipboard");
    }

    private async void OnToggleServerClicked(object sender, RoutedEventArgs e)
    {
        var services = ((App)Application.Current).Services;
        if (services.ServerManager.IsRunning)
        {
            await services.ServerManager.StopAsync();
            services.NotificationService.Show("Server Stopped", "VibeProxy is now stopped");
        }
        else
        {
            var success = await services.ServerManager.StartAsync();
            services.NotificationService.Show(success ? "Server Started" : "Server Failed",
                success ? "VibeProxy is now running" : "Could not start server");
        }
    }

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ServiceViewModel service)
        {
            await HandleConnectAsync(service);
        }
    }

    private async Task HandleConnectAsync(ServiceViewModel service)
    {
        var app = (App)Application.Current;
        var viewModel = ViewModel;

        if (service.Type == ServiceType.Qwen)
        {
            var email = await PromptAsync("Qwen Account Email", "Enter your Qwen account email address");
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }
            var result = await viewModel.StartAuthAsync(service.Type, qwenEmail: email);
            await HandleAuthResultAsync(result);
            return;
        }

        if (service.Type == ServiceType.Zai)
        {
            var apiKey = await PromptAsync("Z.AI API Key", "Enter your Z.AI API key");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return;
            }
            var result = await viewModel.StartAuthAsync(service.Type, zaiApiKey: apiKey);
            await HandleAuthResultAsync(result);
            return;
        }

        var authResult = await viewModel.StartAuthAsync(service.Type);
        await HandleAuthResultAsync(authResult);
    }

    private async Task HandleRemoveAsync(AuthAccountViewModel account)
    {
        var dialog = new ContentDialog
        {
            Title = "Remove Account",
            Content = $"Are you sure you want to remove {account.DisplayName}?",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var app = (App)Application.Current;
            var wasRunning = app.Services.ServerManager.IsRunning;
            if (wasRunning)
            {
                await app.Services.ServerManager.StopAsync();
            }

            if (app.Services.AuthManager.DeleteAccount(account.Account))
            {
                await app.Services.AuthManager.RefreshAsync();
                await ShowResultAsync(true, $"Removed {account.DisplayName}");
            }
            else
            {
                await ShowResultAsync(false, "Failed to remove account");
            }

            if (wasRunning)
            {
                await app.Services.ServerManager.StartAsync();
            }
        }
    }

    private async Task<string?> PromptAsync(string title, string message)
    {
        var input = new TextBox { PlaceholderText = "" };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = message },
                    input
                }
            },
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? input.Text : null;
    }

    private async Task ShowResultAsync(bool success, string message)
    {
        var dialog = new ContentDialog
        {
            Title = success ? "Success" : "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async Task HandleAuthResultAsync(VibeProxy.Core.Services.AuthResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.DeviceCode))
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(result.DeviceCode);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }

        await ShowResultAsync(result.Ok, result.Message);
    }
}
