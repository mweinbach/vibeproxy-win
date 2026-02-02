using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VibeProxy.WinUI.Views;

public sealed partial class MainPage
{
    internal void OnOpenAuthFolderClicked(object sender, RoutedEventArgs e)
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

    internal void OnCopyUrlClicked(object sender, RoutedEventArgs e)
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

    internal async void OnToggleServerClicked(object sender, RoutedEventArgs e)
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
}
