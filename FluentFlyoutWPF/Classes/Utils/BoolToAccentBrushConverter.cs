// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FluentFlyoutWPF.Classes.Utils;

public class BoolToAccentBrushConverter : IValueConverter
{
    public double ActiveOpacity { get; set; } = 0.1;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // If the setting to highlight active apps in the volume mixer is disabled, return transparent
        if (!SettingsManager.Current.VolumeMixerHighlightActiveApps) return Brushes.Transparent;

        if (value is bool isActive && isActive)
        {
            if (Application.Current.TryFindResource("AccentTextFillColorPrimaryBrush") is SolidColorBrush accentBrush)
            {
                return new SolidColorBrush(accentBrush.Color) { Opacity = ActiveOpacity };
            }
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
