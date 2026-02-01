using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VibeProxy.WinUI.ViewModels;
using VibeProxy.WinUI.Views;
using WinUIEx;

namespace VibeProxy.WinUI;

public sealed class MainWindow : WindowEx
{
    public MainWindow(MainViewModel viewModel)
    {
        Title = "VibeProxy";
        
        // Enable Mica backdrop (safe fallback if unavailable)
        try
        {
            SystemBackdrop = new MicaBackdrop();
            ExtendsContentIntoTitleBar = true;
        }
        catch (Exception ex)
        {
            VibeProxy.WinUI.Services.LogService.Write("Mica backdrop unavailable", ex);
        }
        
        var page = new MainPage(viewModel);
        viewModel.AttachDispatcher(DispatcherQueue);
        Content = page;

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 850));
        CenterOnScreen();
        
        // Set the app icon explicitly for the taskbar
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.scale-200.png");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        AppWindow.Closing += OnClosing;
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        AppWindow.Hide();
    }

    public void ShowWindow()
    {
        CenterOnScreen();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(1000);
                presenter.IsAlwaysOnTop = false;
            });
        }

        AppWindow.Show();
        Activate();
    }

    private void CenterOnScreen()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null)
        {
            return;
        }

        var workArea = displayArea.WorkArea;
        var size = AppWindow.Size;
        var x = workArea.X + Math.Max(0, (workArea.Width - size.Width) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - size.Height) / 2);
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }
}
