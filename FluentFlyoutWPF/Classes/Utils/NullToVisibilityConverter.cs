// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FluentFlyoutWPF.Classes.Utils;

public class NullToVisibilityConverter : IValueConverter
{
    public Visibility NullValue { get; set; } = Visibility.Visible;
    public Visibility NonNullValue { get; set; } = Visibility.Collapsed;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? NullValue : NonNullValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
