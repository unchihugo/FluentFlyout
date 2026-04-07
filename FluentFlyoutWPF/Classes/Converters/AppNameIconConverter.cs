// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows.Data;

using FluentFlyout.Classes.Utils;

namespace FluentFlyoutWPF.Classes.Converters;

public class AppNameIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string appName && !string.IsNullOrEmpty(appName))
        {
            // First check against active processes or in-memory cache.
            var (title, icon) = MediaPlayerData.getMediaPlayerData(appName);
            if (icon != null) return icon;

            // If that fails, check the old disk cache.
            return MediaPlayerData.GetIconFromDisk(appName);
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}