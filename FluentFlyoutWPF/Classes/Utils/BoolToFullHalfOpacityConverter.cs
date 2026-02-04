// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows.Data;

namespace FluentFlyoutWPF.Classes.Utils;

public class BoolToFullHalfOpacityConverter : IValueConverter
{
    public double TrueValue { get; set; } = 1;
    public double FalseValue { get; set; } = 0.5;

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
        if (value is double opacity)
        {
            return opacity == TrueValue;
        }
        return false;
    }
}
