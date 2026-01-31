using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace VibeProxy.WinUI.Helpers;

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return string.Empty;
    }
}