using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace VibeProxy.WinUI.Helpers;

public sealed class StatusToSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isRunning)
        {
            return isRunning ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        }

        return InfoBarSeverity.Informational;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
