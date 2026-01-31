using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using VibeProxy.WinUI.ViewModels;
using VibeProxy.WinUI.Views;
using WinUIEx;

namespace VibeProxy.WinUI;

public sealed class MainWindow : WindowEx
{
    public MainWindow(MainViewModel viewModel)
    {
        Title = "VibeProxy";
        var page = new MainPage();
        page.Initialize(viewModel);
        Content = page;

        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 760));
        AppWindow.Closing += OnClosing;
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        AppWindow.Hide();
    }

    public void ShowWindow()
    {
        AppWindow.Show();
        Activate();
    }
}
