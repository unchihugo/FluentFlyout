// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;

using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class AppFilteringPage : Page 
{
    public AppFilteringPage() 
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }

    private static void SaveAndRefreshMedia()
    {
        SettingsManager.SaveSettings();
        var mainWindow = Application.Current.MainWindow as MainWindow;

        mainWindow?.RefreshFilteredMedia();
    }

    private static void PopulateComboBox(ComboBox comboBox) 
    {
        var mainWindow = Application.Current.MainWindow as MainWindow;

        if (mainWindow?.mediaManager == null) return;

        var apps = mainWindow.mediaManager.CurrentMediaSessions.Values
            .Select(s => MediaPlayerData.getMediaPlayerData(s.Id).Item1)
            .Distinct()
            .OrderBy(a => a)
            .ToList();

        comboBox.ItemsSource = apps;
    }

    private void AllowComboBox_DropDownOpened(object sender, System.EventArgs e) 
    {
        PopulateComboBox(AllowComboBox);
    }

    private void BlockComboBox_DropDownOpened(object sender, System.EventArgs e) 
    {
        PopulateComboBox(BlockComboBox);
    }

    private static string NormalizeAppName(string app)
    {
        if (app.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase))
        {
            app = app[..^4];
        }

        var mainWindow = Application.Current.MainWindow as MainWindow;
        if (mainWindow?.mediaManager == null) return app;

        var match = mainWindow.mediaManager.CurrentMediaSessions.Values
            .Select(s => MediaPlayerData.getMediaPlayerData(s.Id).Item1)
            .FirstOrDefault(name => name.Equals(app, System.StringComparison.OrdinalIgnoreCase) || 
                                    name.Contains(app, System.StringComparison.OrdinalIgnoreCase) || 
                                    app.Contains(name, System.StringComparison.OrdinalIgnoreCase));

        return match ?? app;
    }

    private void AddAllow_Click(object sender, RoutedEventArgs e) 
    {
        var app = AllowComboBox.SelectedItem?.ToString()?.Trim();

        if (string.IsNullOrEmpty(app) || SettingsManager.Current.AllowedApps.Any(a => a.Equals(app, System.StringComparison.OrdinalIgnoreCase))) return;

        SettingsManager.Current.AllowedApps.Add(app);
        AllowComboBox.SelectedIndex = -1;

        SaveAndRefreshMedia();
    }

    private void AddAllowManual_Click(object sender, RoutedEventArgs e) 
    {
        var app = AllowTextBox.Text?.Trim();

        if (string.IsNullOrEmpty(app)) return;

        app = NormalizeAppName(app);

        if (SettingsManager.Current.AllowedApps.Any(a => a.Equals(app, System.StringComparison.OrdinalIgnoreCase))) return;

        SettingsManager.Current.AllowedApps.Add(app);
        AllowTextBox.Text = string.Empty;

        SaveAndRefreshMedia();
    }

    private void RemoveAllow_Click(object sender, RoutedEventArgs e) 
    {
        if (sender is not Button { Tag: string app }) return;

        SettingsManager.Current.AllowedApps.Remove(app);
        SaveAndRefreshMedia();
    }

    private void AddBlock_Click(object sender, RoutedEventArgs e) 
    {
        var app = BlockComboBox.SelectedItem?.ToString()?.Trim();

        if (string.IsNullOrEmpty(app) || SettingsManager.Current.BlockedApps.Any(b => b.Equals(app, System.StringComparison.OrdinalIgnoreCase))) return;

        SettingsManager.Current.BlockedApps.Add(app);
        BlockComboBox.SelectedIndex = -1;

        SaveAndRefreshMedia();
    }

    private void AddBlockManual_Click(object sender, RoutedEventArgs e) 
    {
        var app = BlockTextBox.Text?.Trim();

        if (string.IsNullOrEmpty(app)) return;

        app = NormalizeAppName(app);

        if (SettingsManager.Current.BlockedApps.Any(b => b.Equals(app, System.StringComparison.OrdinalIgnoreCase))) return;

        SettingsManager.Current.BlockedApps.Add(app);
        BlockTextBox.Text = string.Empty;

        SaveAndRefreshMedia();
    }

    private void RemoveBlock_Click(object sender, RoutedEventArgs e) 
    {
        if (sender is not Button { Tag: string app }) return;

        SettingsManager.Current.BlockedApps.Remove(app);

        SaveAndRefreshMedia();
    }
}