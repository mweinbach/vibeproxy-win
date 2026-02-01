using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VibeProxy.Core.Models;
using VibeProxy.Core.Services;
using VibeProxy.WinUI.ViewModels;

namespace VibeProxy.WinUI.Views;

public sealed partial class MainPage : Page
{
    private readonly CancellationTokenSource _pageCts = new();
    private bool _isDisposed;

    public MainViewModel ViewModel { get; }

    public MainPage(MainViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        InitializeComponent();
        
        ViewModel.ConnectFlowRequested += HandleConnectAsync;
        ViewModel.RemoveFlowRequested += HandleRemoveAsync;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            NavView.Header = item.Content;
            var tag = item.Tag?.ToString();
            DashboardSection.Visibility = tag == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
            ServicesSection.Visibility = tag == "Services" ? Visibility.Visible : Visibility.Collapsed;
            SettingsSection.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
            AboutSection.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
            
            ContentScrollViewer.ScrollToVerticalOffset(0);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await UpdateLaunchToggleAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update launch toggle: {ex.Message}");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isDisposed = true;
        _pageCts.Cancel();
        _pageCts.Dispose();
        
        // Unsubscribe from ViewModel events
        if (ViewModel != null)
        {
            ViewModel.ConnectFlowRequested -= HandleConnectAsync;
            ViewModel.RemoveFlowRequested -= HandleRemoveAsync;
        }
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
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update startup task: {ex.Message}");
        }
    }

    private void OnOpenAuthFolderClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var authDir = ((App)Application.Current).Services.AuthManager.AuthDirectory;
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{authDir}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open auth folder: {ex.Message}");
        }
    }

    private void OnCopyUrlClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var services = ((App)Application.Current).Services;
            var url = $"http://localhost:{services.ServerManager.ProxyPort}";
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(url);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            services.NotificationService.Show("Copied", "Server URL copied to clipboard");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to copy URL: {ex.Message}");
        }
    }

    private async void OnToggleServerClicked(object sender, RoutedEventArgs e)
    {
        if (_isDisposed) return;

        var services = ((App)Application.Current).Services;
        
        try
        {
            if (services.ServerManager.IsRunning)
            {
                await services.ServerManager.StopAsync(_pageCts.Token);
                services.NotificationService.Show("Server Stopped", "VibeProxy is now stopped");
            }
            else
            {
                var success = await services.ServerManager.StartAsync(_pageCts.Token);
                var message = success
                    ? "VibeProxy is now running"
                    : services.ServerManager.Logs.LastOrDefault() ?? "Could not start server";
                services.NotificationService.Show(success ? "Server Started" : "Server Failed", message);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on page unload
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to toggle server: {ex.Message}");
            services.NotificationService.Show("Error", $"Failed to toggle server: {ex.Message}");
        }
    }

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        if (_isDisposed) return;

        if (sender is FrameworkElement element && element.DataContext is ServiceViewModel service)
        {
            try
            {
                await HandleConnectAsync(service);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Connect failed: {ex.Message}");
            }
        }
    }

    private async Task HandleConnectAsync(ServiceViewModel service)
    {
        if (_isDisposed) return;

        var app = (App)Application.Current;
        var viewModel = ViewModel;

        try
        {
            if (service.Type == ServiceType.Qwen)
            {
                var email = await PromptAsync("Qwen Account Email", "Enter your Qwen account email address");
                if (string.IsNullOrWhiteSpace(email))
                {
                    return;
                }
                var result = await viewModel.StartAuthAsync(service.Type, qwenEmail: email, cancellationToken: _pageCts.Token);
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
                var result = await viewModel.StartAuthAsync(service.Type, zaiApiKey: apiKey, cancellationToken: _pageCts.Token);
                await HandleAuthResultAsync(result);
                return;
            }

            var authResult = await viewModel.StartAuthAsync(service.Type, cancellationToken: _pageCts.Token);
            await HandleAuthResultAsync(authResult);
        }
        catch (OperationCanceledException)
        {
            // Expected on page unload
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Auth failed: {ex.Message}");
            await ShowResultAsync(false, $"Authentication failed: {ex.Message}");
        }
    }

    private async Task HandleRemoveAsync(AuthAccountViewModel account)
    {
        if (_isDisposed) return;

        try
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
                    await app.Services.ServerManager.StopAsync(_pageCts.Token);
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
                    await app.Services.ServerManager.StartAsync(_pageCts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on page unload
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Remove failed: {ex.Message}");
            await ShowResultAsync(false, $"Failed to remove account: {ex.Message}");
        }
    }

    private async Task<string?> PromptAsync(string title, string message)
    {
        if (_isDisposed) return null;

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
        if (_isDisposed) return;

        var dialog = new ContentDialog
        {
            Title = success ? "Success" : "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async Task HandleAuthResultAsync(AuthResult result)
    {
        if (_isDisposed) return;

        if (!string.IsNullOrWhiteSpace(result.DeviceCode))
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(result.DeviceCode);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }

        await ShowResultAsync(result.Ok, result.Message);
    }
}
