// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows.Data;

namespace FluentFlyoutWPF.Classes.Utils;

public class DecimalPercentageToFullConverter : IValueConverter
{
    // Convert decimal percentage (0.5) to full percentage (50)
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return 0;
        if (value is float floatValue)
        {
            return (int)Math.Round(floatValue * 100);
        }
        else
        {
            return 0;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float floatValue)
        {
            return floatValue / 100;
        }
        return 0;
    }
}
