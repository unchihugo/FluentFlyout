// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.Classes.Utils;

public class VolumeToSpeakerSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float volume)
        {
            return volume switch
            {
                < 0.10f => SymbolRegular.Speaker028,
                < 0.50f => SymbolRegular.Speaker128,
                _ => SymbolRegular.Speaker228
            };
        }

        return SymbolRegular.Speaker228;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
