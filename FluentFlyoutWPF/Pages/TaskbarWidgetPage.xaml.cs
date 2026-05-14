// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluentFlyoutWPF.Pages;

public partial class TaskbarWidgetPage : Page
{
    public TaskbarWidgetPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
        SystemStatsFontFamilyComboBox.ItemsSource = Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Distinct()
            .OrderBy(font => font, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        UpdateMonitorList();
    }

    private async void UnlockPremiumButton_Click(object sender, RoutedEventArgs e)
    {
        LicenseManager.UnlockPremium(sender);
    }

    private void UpdateMonitorList()
    {
        MonitorUtil.UpdateMonitorList(
            TaskbarWidgetSelectedMonitorComboBox,
            () => SettingsManager.Current.TaskbarWidgetSelectedMonitor,
            value => SettingsManager.Current.TaskbarWidgetSelectedMonitor = value);
    }
}
