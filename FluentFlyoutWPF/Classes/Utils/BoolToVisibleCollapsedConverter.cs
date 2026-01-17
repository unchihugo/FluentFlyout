using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FluentFlyoutWPF.Classes.Utils;

public class BoolToVisibleCollapsedConverter : IValueConverter
{
    public Visibility TrueValue { get; set; } = Visibility.Visible;
    public Visibility FalseValue { get; set; } = Visibility.Collapsed;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return FalseValue;
        if (value is bool boolValue)
        {
            return boolValue ? TrueValue : FalseValue;
        }
        else
        {
            return FalseValue;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == TrueValue;
        }
        return false;
    }
}
