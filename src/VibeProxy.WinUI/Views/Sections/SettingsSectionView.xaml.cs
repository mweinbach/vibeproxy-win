using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VibeProxy.WinUI.ViewModels;

namespace VibeProxy.WinUI.Views.Sections;

public sealed partial class SettingsSectionView : UserControl
{
    public MainViewModel ViewModel
    {
        get => (MainViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(MainViewModel),
        typeof(SettingsSectionView),
        new PropertyMetadata(null));

    public SettingsSectionView()
    {
        InitializeComponent();
    }

    public async Task UpdateLaunchToggleAsync()
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
}
