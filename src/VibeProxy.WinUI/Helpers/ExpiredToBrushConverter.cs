using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace VibeProxy.WinUI.Helpers;

public sealed class ExpiredToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isExpired = value is bool flag && flag;
        return new SolidColorBrush(isExpired ? Colors.Orange : Colors.Green);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return false;
    }
}