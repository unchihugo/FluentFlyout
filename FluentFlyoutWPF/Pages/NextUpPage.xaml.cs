// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class NextUpPage : Page
{
    public NextUpPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }
}
