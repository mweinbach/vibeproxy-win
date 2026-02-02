using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VibeProxy.WinUI.ViewModels;
using VibeProxy.WinUI.Views;

namespace VibeProxy.WinUI.Views.Sections;

public sealed partial class DashboardSectionView : UserControl
{
    public MainViewModel ViewModel
    {
        get => (MainViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(MainViewModel),
        typeof(DashboardSectionView),
        new PropertyMetadata(null));

    public MainPage Host
    {
        get => (MainPage)GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    public static readonly DependencyProperty HostProperty = DependencyProperty.Register(
        nameof(Host),
        typeof(MainPage),
        typeof(DashboardSectionView),
        new PropertyMetadata(null));

    public DashboardSectionView()
    {
        InitializeComponent();
    }
}
