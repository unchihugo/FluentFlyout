// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows.Data;
using FluentFlyoutWPF.Classes;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.Classes.Converters;

public class VolumeToSpeakerSymbolConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        float volume = values.Length > 0 && values[0] is float v ? v : 1f;
        var endpointType = values.Length > 1 && values[1] is AudioEndpointType t
            ? t
            : AudioEndpointType.Speakers;

        return endpointType switch
        {
            AudioEndpointType.Headphones => volume switch
            {
                < 0.10f => SymbolRegular.Speaker028,
                _ => SymbolRegular.Headphones28
            },
            AudioEndpointType.DigitalDisplay => volume switch
            {
                < 0.10f => SymbolRegular.Speaker028,
                _ => SymbolRegular.DesktopSpeaker28
            },
            _ => volume switch
            {
                < 0.10f => SymbolRegular.Speaker028,
                < 0.50f => SymbolRegular.Speaker128,
                _ => SymbolRegular.Speaker228
            }
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
