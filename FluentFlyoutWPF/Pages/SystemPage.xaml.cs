// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using NLog;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace FluentFlyoutWPF.Pages;

public partial class SystemPage : Page
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public SystemPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
        UpdateMonitorList();
    }

    private async void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        await SetStartupAsync(StartupSwitch.IsChecked ?? false);
    }

    private async Task SetStartupAsync(bool enable)
    {
        StartupManager.StartupOperationResult result = await StartupManager.ApplyAsync(enable);
        if (result.Success)
            return;

        // Keep the persisted setting and the switch aligned with the state we
        // could verify. A failed enable must not leave the toggle checked.
        bool actualState = result.IsEnabled ?? !enable;
        SettingsManager.Current.Startup = actualState;
        StartupSwitch.IsChecked = actualState;

        MessageBox messageBox = new()
        {
            Title = "Error",
            Content = $"Failed to set startup: {result.Error ?? "Windows did not confirm the requested state."}",
            CloseButtonText = "OK",
        };

        _ = messageBox.ShowDialogAsync();
    }

    private void StartupHyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void ToggleSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.ApplyTrayVisibilityPolicy();
    }

    private void UpdateMonitorList()
    {
        MonitorUtil.UpdateMonitorList(
            FlyoutSelectedMonitorComboBox,
            () => SettingsManager.Current.FlyoutSelectedMonitor,
            value => SettingsManager.Current.FlyoutSelectedMonitor = value);
    }


    private async void ExportButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"FluentFlyout_Settings_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}",
            DefaultExt = ".xml",
            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                await SettingsManager.SaveSettingsAsync(saveFileDialog.FileName);

                Wpf.Ui.Controls.MessageBox messageBox = new()
                {
                    Title = Application.Current.FindResource("ExportSuccessful").ToString(),
                    Content = Application.Current.FindResource("SettingsExportedSuccessfully").ToString(),
                    CloseButtonText = "OK",
                };

                _ = messageBox.ShowDialogAsync();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error exporting settings");

                Wpf.Ui.Controls.MessageBox messageBox = new()
                {
                    Title = Application.Current.FindResource("ExportFailed").ToString(),
                    Content = Application.Current.FindResource("FailedToExportSettings").ToString(),
                    CloseButtonText = "OK",
                };

                _ = messageBox.ShowDialogAsync();
            }
        }
    }

    private async void ImportButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            DefaultExt = ".xml",
            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            Wpf.Ui.Controls.MessageBox confirmBox = new()
            {
                Title = Application.Current.FindResource("ImportSettings").ToString(),
                Content = Application.Current.FindResource("ImportSettingsWarning").ToString(),
                CloseButtonText = "No",
                SecondaryButtonText = "Yes",
            };

            var result = await confirmBox.ShowDialogAsync();

            if (result == Wpf.Ui.Controls.MessageBoxResult.Secondary)
            {
                try
                {
                    SettingsManager.RestoreSettings(openFileDialog.FileName);
                    await SettingsManager.SaveSettingsAsync();

                    Wpf.Ui.Controls.MessageBox messageBox = new()
                    {
                        Title = Application.Current.FindResource("ImportSuccessful").ToString(),
                        Content = Application.Current.FindResource("SettingsImportedSuccessfully").ToString(),
                        CloseButtonText = "OK",
                    };

                    _ = messageBox.ShowDialogAsync();

                    // Restart the application
                    Application.Current.Shutdown();
                    System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error importing settings");

                    Wpf.Ui.Controls.MessageBox messageBox = new()
                    {
                        Title = Application.Current.FindResource("ImportFailed").ToString(),
                        Content = Application.Current.FindResource("FailedToImportSettings").ToString(),
                        CloseButtonText = "OK",
                    };

                    _ = messageBox.ShowDialogAsync();
                }
            }
        }
    }

    private void AppFiltering_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(AppFilteringPage));
    }

    private void Advanced_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(AdvancedPage));
    }
}