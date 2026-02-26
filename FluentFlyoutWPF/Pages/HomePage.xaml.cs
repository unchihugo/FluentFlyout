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
                Application.Current.FindResource("LastChecked")?.ToString() ?? "Last checked: {0}",
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
            string url = !string.IsNullOrEmpty(UpdateState.Current.UpdateUrl) ? UpdateState.Current.UpdateUrl : "https://fluentflyout.com/changelog/";
            UpdateChecker.OpenUpdateUrl(url);
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
            UpdateStatusText.Text = Application.Current.FindResource("CheckingForUpdates")?.ToString() ?? "Checking...";

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
        NavigateToPage(typeof(MediaFlyoutPage));
    }

    private void TaskbarWidget_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        NavigateToPage(typeof(TaskbarWidgetPage));
    }

    private void NextUp_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        NavigateToPage(typeof(NextUpPage));
    }

    private void LockKeys_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        NavigateToPage(typeof(LockKeysPage));
    }

    private void TaskbarVisualizer_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        NavigateToPage(typeof(TaskbarVisualizerPage));
    }

    private void System_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        NavigateToPage(typeof(SystemPage));
    }

    private void NavigateToPage(Type pageType)
    {
        var window = System.Windows.Window.GetWindow(this) as SettingsWindow;
        var navigationView = window?.FindName("RootNavigation") as NavigationView;
        navigationView?.Navigate(pageType);
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
}