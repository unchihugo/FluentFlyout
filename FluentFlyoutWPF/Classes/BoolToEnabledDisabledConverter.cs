using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FluentFlyout.Classes;

public class BoolToEnabledDisabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            var enabledKey = "Enabled";
            var disabledKey = "Disabled";
            
            var resourceKey = boolValue ? enabledKey : disabledKey;
            
            if (Application.Current.TryFindResource(resourceKey) is string localizedString)
            {
                return localizedString;
            }
            
            return boolValue ? "Enabled" : "Disabled";
        }
        
        return "Disabled";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
