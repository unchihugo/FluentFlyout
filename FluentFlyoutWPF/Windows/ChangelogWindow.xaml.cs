// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Services;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.Windows;

public partial class ChangelogWindow : FluentWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly string _lastViewedVersion;
    private readonly string _currentVersion;
    private readonly CancellationTokenSource _loadCancellation = new();
    private readonly Stopwatch _openDuration = Stopwatch.StartNew();
    private string _viewAllUrl = string.Empty;

    public ChangelogWindow(string lastViewedVersion, string currentVersion)
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
        _lastViewedVersion = lastViewedVersion;
        _currentVersion = currentVersion;
    }

    private async void ChangelogWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var feed = await ChangelogService.GetReleasesAsync(
                _lastViewedVersion,
                _loadCancellation.Token);

            if (_loadCancellation.IsCancellationRequested)
            {
                return;
            }

            ReleaseNavigation.ItemsSource = feed.Releases;
            ReleaseContentBrowser.Visibility = Visibility.Visible;
            StatusPanel.Visibility = Visibility.Collapsed;
            _viewAllUrl = feed.ViewAllUrl;
            ViewAllFooter.Visibility = Visibility.Visible;
            ReleaseNavigation.SelectedIndex = 0;

            if (ChangelogService.HasUnseenUpdate(_currentVersion, _lastViewedVersion))
            {
                SettingsManager.Current.LastViewedChangelogVersion = _currentVersion;
                SettingsManager.SaveSettings();
            }
        }
        catch (OperationCanceledException) when (_loadCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load changelog releases");
            StatusText.Text = Application.Current.TryFindResource("ChangelogLoadFailedText")?.ToString()
                ?? "The changelog could not be loaded.";
            OpenOnlineButton.Visibility = Visibility.Visible;
        }
    }

    private void ReleaseNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReleaseNavigation.SelectedItem is not ChangelogRelease release)
        {
            return;
        }

        bool isDarkTheme = ApplicationThemeManager.GetAppTheme() != ApplicationTheme.Light;
        ReleaseContentBrowser.NavigateToString(
            ChangelogService.CreateHtmlDocument(release, isDarkTheme));
    }

    private void ReleaseContentBrowser_Navigating(
        object sender,
        System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (e.Uri == null || string.Equals(e.Uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        if (string.Equals(e.Uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            Notifications.OpenUrlInBrowser(e.Uri.AbsoluteUri);
        }
    }

    private void OpenOnlineButton_Click(object sender, RoutedEventArgs e)
    {
        Notifications.OpenChangelogInBrowser();
    }

    private void ViewAllButton_Click(object sender, RoutedEventArgs e)
    {
        Notifications.OpenUrlInBrowser(_viewAllUrl);
    }

    private void ChangelogWindow_Closed(object? sender, EventArgs e)
    {
        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
        _openDuration.Stop();

        _ = TelemetryService.SendTelemetryEventAsync(
            "changelog_window_duration",
            eventData: new Dictionary<string, object>
            {
                ["durationMs"] = _openDuration.ElapsedMilliseconds
            });
    }
}
