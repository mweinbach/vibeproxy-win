using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

        DashboardSection.Host = this;
        
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
            await SettingsSection.UpdateLaunchToggleAsync();
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

}
