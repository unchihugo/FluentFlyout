// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class TaskbarWidgetPage : Page
{
    public TaskbarWidgetPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
        UpdateMonitorList();
    }

    private async void UnlockPremiumButton_Click(object sender, RoutedEventArgs e)
    {
        LicenseManager.UnlockPremium(sender);
    }

    // TODO: merge with SystemPage.xaml.cs UpdateMonitorList() function as they're very similar
    private void UpdateMonitorList()
    {
        var monitors = WindowHelper.GetMonitors();
        TaskbarWidgetSelectedMonitorComboBox.Items.Clear();

        var resetToPrimary = SettingsManager.Current.TaskbarWidgetSelectedMonitor >= monitors.Count || 
                           SettingsManager.Current.TaskbarWidgetSelectedMonitor < 0;
        int selectedMonitor = SettingsManager.Current.TaskbarWidgetSelectedMonitor;

        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var cb = new ComboBoxItem()
            {
                Content = monitor.isPrimary ? (i + 1).ToString() + " *" : (i + 1).ToString(),
            };
            if (resetToPrimary && monitor.isPrimary)
                selectedMonitor = i;

            TaskbarWidgetSelectedMonitorComboBox.Items.Add(cb);
        }

        TaskbarWidgetSelectedMonitorComboBox.SelectedIndex = selectedMonitor;
        SettingsManager.Current.TaskbarWidgetSelectedMonitor = selectedMonitor;
    }
}
