// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using NLog;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Windows.ApplicationModel;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace FluentFlyoutWPF.Pages;

public partial class HomePage : Page
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public HomePage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;

        try
        {
            var version = Package.Current.Id.Version;
            VersionTextBlock.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            VersionTextBlock.Text = "debug version";
        }

        UpdateLastCheckedText();
    }

    private void UpdateLastCheckedText()
    {
        if (UpdateState.Current.LastUpdateCheck != default)
        {
            LastCheckedText.Text = string.Format(
                Application.Current.FindResource("LastChecked")?.ToString(),
                UpdateState.Current.LastCheckedText);
        }
        else
        {
            LastCheckedText.Text = string.Empty;
        }
    }

    private void ViewUpdates_Click(object sender, RoutedEventArgs e)
    {
        Notifications.OpenChangelogInBrowser();
    }

    private long _lastChecked = 0;

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        // prevent multiple clicks within 1 second
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _lastChecked < 1)
        {
            return;
        }

        _lastChecked = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (UpdateState.Current.IsUpdateAvailable)
        {
#if GITHUB_RELEASE
            await StartAutoUpdateAsync();
#else
            string url = !string.IsNullOrEmpty(UpdateState.Current.UpdateUrl) ? UpdateState.Current.UpdateUrl : "https://fluentflyout.com/changelog/";
            UpdateChecker.OpenUpdateUrl(url);
#endif
        }
        else
        {
            await CheckForUpdatesAsync();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            UpdateStatusText.Text = Application.Current.FindResource("CheckingForUpdates")?.ToString();

            var result = await UpdateChecker.CheckForUpdatesAsync(SettingsManager.Current.LastKnownVersion);

            if (result.Success)
            {
                UpdateState.Current.IsUpdateAvailable = result.IsUpdateAvailable;
                UpdateState.Current.NewestVersion = result.NewestVersion;
                UpdateState.Current.UpdateUrl = result.UpdateUrl;
                UpdateState.Current.LastUpdateCheck = result.CheckedAt;

                UpdateLastCheckedText();

                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await Task.Delay(500); // slight delay for better UX

                        if (result.IsUpdateAvailable)
                        {
                            UpdateStatusText.Text = Application.Current.FindResource("UpdateAvailableNotificationTitle")?.ToString();
                        }
                        else
                        {
                            UpdateStatusText.Text = Application.Current.FindResource("UpToDate")?.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error while updating update status text on UI thread");
                    }
                });
            }
            else
            {
                UpdateStatusText.Text = Application.Current.FindResource("UpToDate")?.ToString();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to check for updates from HomePage");
            UpdateStatusText.Text = "Unable to check for updates"; // not localized
        }
    }

    private void MediaFlyout_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(MediaFlyoutPage));
    }

    private void TaskbarWidget_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(TaskbarWidgetPage));
    }

    private void NextUp_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(NextUpPage));
    }

    private void LockKeys_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(LockKeysPage));
    }

    private void TaskbarVisualizer_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(TaskbarVisualizerPage));
    }

    private void System_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(SystemPage));
    }

    // same as in AboutPage.xaml.cs
    private async void UnlockPremiumButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        LicenseManager.UnlockPremium(sender);
    }

    private void ViewMicrosoftStore_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://apps.microsoft.com/detail/9N45NSM4TNBP",
                UseShellExecute = true
            });
        }
        catch
        {
            Logger.Error("Failed to open Microsoft Store page");
        }
    }

    private void ViewLogs_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", FileSystemHelper.GetLogsPath());
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open logs folder");
        }
    }

    private void ReportBug_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/unchihugo/FluentFlyout/issues/new/choose",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open bug report page");
        }
    }

#if GITHUB_RELEASE
    private CancellationTokenSource? _downloadCts;

    private async Task StartAutoUpdateAsync()
    {
        if (UpdateState.Current.IsDownloading || UpdateState.Current.IsInstalling)
            return;

        // If already downloaded, show install panel
        if (!string.IsNullOrEmpty(UpdateState.Current.DownloadedBundlePath)
            && File.Exists(UpdateState.Current.DownloadedBundlePath))
        {
            ShowInstallPanel();
            return;
        }

        try
        {
            AutoUpdatePanel.Visibility = Visibility.Visible;
            DownloadProgressPanel.Visibility = Visibility.Visible;
            InstallPanel.Visibility = Visibility.Collapsed;
            InstallingPanel.Visibility = Visibility.Collapsed;
            UpdateErrorBar.IsOpen = false;

            // Fetch the GitHub release asset info
            DownloadStatusText.Text = Application.Current.FindResource("CheckingForUpdates")?.ToString() ?? "Checking...";
            var asset = await UpdateChecker.GetGitHubReleaseAssetAsync();
            if (asset == null)
            {
                ShowUpdateError("Could not find update package on GitHub.");
                return;
            }

            DownloadStatusText.Text = Application.Current.FindResource("AutoUpdateDownloading")?.ToString() ?? "Downloading...";
            _downloadCts = new CancellationTokenSource();

            var progress = new Progress<double>(pct =>
            {
                DownloadProgressBar.Value = pct;
                DownloadStatusText.Text = $"{Application.Current.FindResource("AutoUpdateDownloading")?.ToString() ?? "Downloading..."} {pct:F0}%";
            });

            var filePath = await AutoUpdater.DownloadUpdateAsync(
                asset.DownloadUrl, asset.Size, asset.Name, progress, _downloadCts.Token);

            if (filePath != null)
            {
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                ShowInstallPanel();
            }
            else
            {
                ShowUpdateError(UpdateState.Current.UpdateError);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Auto-update failed");
            ShowUpdateError($"Update failed: {ex.Message}");
        }
    }

    private void ShowInstallPanel()
    {
        AutoUpdatePanel.Visibility = Visibility.Visible;
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        InstallPanel.Visibility = Visibility.Visible;
        InstallingPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowUpdateError(string message)
    {
        AutoUpdatePanel.Visibility = Visibility.Visible;
        UpdateErrorBar.Title = message;
        UpdateErrorBar.IsOpen = true;
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        InstallPanel.Visibility = Visibility.Collapsed;
        InstallingPanel.Visibility = Visibility.Collapsed;
    }

#endif

    // Event handler must exist unconditionally since XAML references it.
    // The auto-update logic is only compiled for GitHub Release builds.
    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
#if GITHUB_RELEASE
        var path = UpdateState.Current.DownloadedBundlePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ShowUpdateError("Update file not found. Please try again.");
            UpdateState.Current.DownloadedBundlePath = string.Empty;
            InstallPanel.Visibility = Visibility.Collapsed;
            return;
        }

        InstallPanel.Visibility = Visibility.Collapsed;
        InstallingPanel.Visibility = Visibility.Visible;
        UpdateErrorBar.IsOpen = false;

        var success = await AutoUpdater.InstallUpdateAsync(path);

        if (!success)
        {
            InstallingPanel.Visibility = Visibility.Collapsed;
            ShowUpdateError(UpdateState.Current.UpdateError);
        }
        // If successful, the app will be shut down by -ForceApplicationShutdown
#else
        await Task.CompletedTask;
#endif
    }
}