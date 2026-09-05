// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Windows.Controls;

namespace FluentFlyout.Pages;

/// <summary>
/// Interaction logic for AdvancedPage.xaml
/// </summary>
public partial class AdvancedPage : Page
{
    public AdvancedPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }
}