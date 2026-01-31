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
        viewModel.AttachDispatcher(DispatcherQueue);
        Content = page;

        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 760));
        CenterOnScreen();
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
