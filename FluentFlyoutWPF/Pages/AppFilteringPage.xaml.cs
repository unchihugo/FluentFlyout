// Copyright � 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class AppFilteringPage : Page {
    public AppFilteringPage() {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }

    private void PopulateComboBox(ComboBox comboBox) {
        var mainWindow = (MainWindow)Application.Current.MainWindow;

        if (mainWindow?.mediaManager != null) {
            var apps = mainWindow.mediaManager.CurrentMediaSessions.Values
                .Select(s => ExtractAppName(s.Id))
                .Distinct()
                .OrderBy(a => a)
                .ToList();

            comboBox.ItemsSource = apps;
        }
    }

    private string ExtractAppName(string id) {
        if (string.IsNullOrEmpty(id)) return "";

        if (id.Contains('\\')) {
            return id.Split('\\').Last();
        }

        if (id.Contains('!')) {
            var parts = id.Split('!');
            if (parts.Length > 1) return parts[1];
        }

        return id;
    }

    private void AllowComboBox_DropDownOpened(object sender, System.EventArgs e) {
        PopulateComboBox(AllowComboBox);
    }

    private void BlockComboBox_DropDownOpened(object sender, System.EventArgs e) {
        PopulateComboBox(BlockComboBox);
    }

    private void AddAllow_Click(object sender, RoutedEventArgs e) {
        var app = AllowComboBox.SelectedItem?.ToString()?.Trim();

        if (!string.IsNullOrEmpty(app) && !SettingsManager.Current.AllowedApps.Contains(app)) {
            SettingsManager.Current.AllowedApps.Add(app);
            AllowComboBox.SelectedIndex = -1;

            SettingsManager.SaveSettings();
        }
    }

    private void AddAllowManual_Click(object sender, RoutedEventArgs e) {
        var app = AllowTextBox.Text?.Trim();

        if (!string.IsNullOrEmpty(app) && !SettingsManager.Current.AllowedApps.Contains(app)) {
            SettingsManager.Current.AllowedApps.Add(app);
            AllowTextBox.Text = string.Empty;

            SettingsManager.SaveSettings();
        }
    }

    private void RemoveAllow_Click(object sender, RoutedEventArgs e) {
        if (sender is Button btn && btn.Tag is string app) {
            SettingsManager.Current.AllowedApps.Remove(app);
            SettingsManager.SaveSettings();
        }
    }

    private void AddBlock_Click(object sender, RoutedEventArgs e) {
        var app = BlockComboBox.SelectedItem?.ToString()?.Trim();

        if (!string.IsNullOrEmpty(app) && !SettingsManager.Current.BlockedApps.Contains(app)) {
            SettingsManager.Current.BlockedApps.Add(app);
            BlockComboBox.SelectedIndex = -1;

            SettingsManager.SaveSettings();
        }
    }

    private void AddBlockManual_Click(object sender, RoutedEventArgs e) {
        var app = BlockTextBox.Text?.Trim();

        if (!string.IsNullOrEmpty(app) && !SettingsManager.Current.BlockedApps.Contains(app)) {
            SettingsManager.Current.BlockedApps.Add(app);
            BlockTextBox.Text = string.Empty;

            SettingsManager.SaveSettings();
        }
    }

    private void RemoveBlock_Click(object sender, RoutedEventArgs e) {
        if (sender is Button btn && btn.Tag is string app) {
            SettingsManager.Current.BlockedApps.Remove(app);

            SettingsManager.SaveSettings();
        }
    }
}