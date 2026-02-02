using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VibeProxy.Core.Models;
using VibeProxy.Core.Services;
using VibeProxy.WinUI.ViewModels;

namespace VibeProxy.WinUI.Views;

public sealed partial class MainPage
{
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
