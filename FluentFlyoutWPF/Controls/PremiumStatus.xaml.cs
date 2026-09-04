// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Settings;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Controls;

public partial class PremiumStatus : UserControl
{
    public PremiumStatus()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }

    private void UnlockPremium_Click(object sender, RoutedEventArgs e) => LicenseManager.UnlockPremium(sender);
}