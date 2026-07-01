// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using NAudio.CoreAudioApi;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class VolumeMixerPage : Page
{
    public VolumeMixerPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }

    private async void UnlockPremiumButton_Click(object sender, RoutedEventArgs e)
    {
        LicenseManager.UnlockPremium(sender);
    }

    /// <summary>
    /// Populates the ComboBox with currently active audio sessions from the default render device.
    /// </summary>
    private static void PopulateIgnoredSourceComboBox(ComboBox comboBox)
    {
        var device = AudioDeviceMonitor.Instance.GetDefaultRenderDevice();
        if (device == null) return;

        try
        {
            var sessions = device.AudioSessionManager.Sessions;
            var names = Enumerable.Range(0, sessions.Count)
                .Select(i => sessions[i])
                .Where(s => s.GetProcessID != 0)
                .Select(GetAudioSessionDisplayName)
                .Where(n => n != "FluentFlyout" && !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            comboBox.ItemsSource = names;
        }
        catch
        {
            // Device or session manager may be unavailable
        }
    }

    /// <summary>
    /// Resolves a human-readable display name for an audio session.
    /// Mirrors the logic in VolumeMixerViewModel.GetSessionDisplayName.
    /// </summary>
    private static string GetAudioSessionDisplayName(AudioSessionControl session)
    {
        if (!string.IsNullOrWhiteSpace(session.DisplayName))
            return session.DisplayName;
        try
        {
            uint pid = session.GetProcessID;
            if (pid != 0)
            {
                var process = Process.GetProcessById((int)pid);
                var mainModule = process.MainModule;
                if (mainModule != null)
                {
                    return !string.IsNullOrWhiteSpace(mainModule.FileVersionInfo.FileDescription)
                        ? mainModule.FileVersionInfo.FileDescription
                        : process.MainWindowTitle is { Length: > 0 } title ? title : process.ProcessName;
                }
                return process.MainWindowTitle is { Length: > 0 } t ? t : process.ProcessName;
            }
        }
        catch
        {
            // Process may have exited
        }
        return "Unknown";
    }

    private void IgnoredSourceComboBox_DropDownOpened(object sender, EventArgs e)
        => PopulateIgnoredSourceComboBox(IgnoredSourceComboBox);

    private void AddIgnoredSource_Click(object sender, RoutedEventArgs e)
    {
        var app = IgnoredSourceComboBox.SelectedItem?.ToString()?.Trim();
        if (string.IsNullOrEmpty(app)) return;
        if (SettingsManager.Current.IgnoredAudioSources
                .Any(a => a.Equals(app, StringComparison.OrdinalIgnoreCase))) return;

        SettingsManager.Current.IgnoredAudioSources.Add(app);
        IgnoredSourceComboBox.SelectedIndex = -1;
        SaveAndRefresh();
    }

    private void AddIgnoredSourceManual_Click(object sender, RoutedEventArgs e)
    {
        var app = IgnoredSourceTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(app)) return;
        if (SettingsManager.Current.IgnoredAudioSources
                .Any(a => a.Equals(app, StringComparison.OrdinalIgnoreCase))) return;

        SettingsManager.Current.IgnoredAudioSources.Add(app);
        IgnoredSourceTextBox.Text = string.Empty;
        SaveAndRefresh();
    }

    private void RemoveIgnoredSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string app }) return;
        SettingsManager.Current.IgnoredAudioSources.Remove(app);
        SaveAndRefresh();
    }

    /// <summary>
    /// Saves settings and triggers a media refresh so that the media flyout and taskbar widget
    /// immediately reflect the updated ignore list.
    /// </summary>
    private static void SaveAndRefresh()
    {
        SettingsManager.SaveSettings();
        var mainWindow = Application.Current.MainWindow as MainWindow;
        mainWindow?.RefreshFilteredMedia();
        // Volume mixer refreshes automatically via IgnoredAudioSources.CollectionChanged in VolumeMixerViewModel
    }
}