using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VibeProxy.WinUI.ViewModels;

namespace VibeProxy.WinUI.Views.Sections;

public sealed partial class ServicesSectionView : UserControl
{
    public MainViewModel ViewModel
    {
        get => (MainViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(MainViewModel),
        typeof(ServicesSectionView),
        new PropertyMetadata(null));

    public ServicesSectionView()
    {
        InitializeComponent();
    }
}
