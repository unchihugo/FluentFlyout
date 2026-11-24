// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.Classes.Converters;

public class EndpointTypeToMuteSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var endpointType = value is AudioEndpointType t ? t : AudioEndpointType.Speakers;

        return endpointType switch
        {
            AudioEndpointType.DigitalDisplay => SymbolRegular.DesktopSpeakerOff24,
            _ => SymbolRegular.SpeakerMute28
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}