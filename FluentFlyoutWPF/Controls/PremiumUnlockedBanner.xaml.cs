// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.ViewModels;
using System.Windows.Controls;

namespace FluentFlyout.Controls;

public partial class PremiumUnlockedBanner : UserControl
{
    public UserSettings UserSettings => SettingsManager.Current;

    public PremiumUnlockedBanner()
    {
        InitializeComponent();
    }
}