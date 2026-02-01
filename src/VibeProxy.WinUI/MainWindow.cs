using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VibeProxy.WinUI.Services;
using VibeProxy.WinUI.ViewModels;
using VibeProxy.WinUI.Views;
using WinUIEx;

namespace VibeProxy.WinUI;

public sealed class MainWindow : WindowEx
{
    private readonly TrayService? _trayService;
    private const string TaskbarLightIcon = "icon-taskbar-light.ico";
    private const string TaskbarDarkIcon = "icon-taskbar-dark.ico";
    private string? _currentIconPath;

    public MainWindow(MainViewModel viewModel, TrayService trayService)
    {
        _trayService = trayService;
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

        void ApplyTheme(ElementTheme theme)
        {
            UpdateTaskbarIcon(theme);
            viewModel.UpdateIconTheme(theme);
        }

        if (page is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) => ApplyTheme(root.ActualTheme);
            ApplyTheme(root.ActualTheme);
        }
        else
        {
            ApplyTheme(ElementTheme.Default);
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

    private void UpdateTaskbarIcon(ElementTheme theme)
    {
        var useLightIcon = theme switch
        {
            ElementTheme.Dark => true,
            ElementTheme.Light => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark
        };

        var iconName = useLightIcon ? TaskbarLightIcon : TaskbarDarkIcon;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", iconName);

        if (!File.Exists(iconPath))
        {
            var fallback = Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.scale-200.png");
            if (!File.Exists(fallback))
            {
                return;
            }

            iconPath = fallback;
        }

        if (string.Equals(iconPath, _currentIconPath, StringComparison.OrdinalIgnoreCase))
        {
            _trayService?.UpdateTheme(theme);
            return;
        }

        AppWindow.SetIcon(iconPath);
        _currentIconPath = iconPath;
        _trayService?.UpdateTheme(theme);
    }
}
