using MicaWPF.Controls;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Windows.ApplicationModel;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes;


namespace FluentFlyoutWPF;

public partial class SettingsWindow : MicaWindow
{
    private static SettingsWindow? instance; // for singleton

    public SettingsWindow()
    {
        if (instance != null)
        {
            if (instance.WindowState == WindowState.Minimized)
            {
                instance.WindowState = WindowState.Normal;
            }

            instance.Activate();
            instance.Focus();
            Close();
            return;
        }

        InitializeComponent();
        instance = this;

        Closed += (s, e) => instance = null;
        DataContext = SettingsManager.Current;
        try // gets the version of the app, works only in release mode
        {
            var version = Package.Current.Id.Version;
            VersionTextBlock.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            VersionTextBlock.Text = "debug version";
        }

        ThemeManager.ApplySavedTheme();
    }

    public static void ShowInstance()
    {
        if (instance == null)
        {
            new SettingsWindow().Show();
            instance?.Activate();
        }
        else
        {
            if (instance.WindowState == WindowState.Minimized)
            {
                instance.WindowState = WindowState.Normal;
            }

            instance.Activate();
            instance.Focus();
        }
    }

    private void SettingsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SettingsManager.SaveSettings();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.SaveSettings();
        Close();
    }

    private void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        // might not work if installed using MSIX, needs investigation
        SetStartup(StartupSwitch.IsChecked ?? false);
        SettingsManager.Current.Startup = StartupSwitch.IsChecked ?? false;
    }

    private void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            const string appName = "FluentFlyout";
            var executablePath = Environment.ProcessPath;

            if (enable)
            {
                // Check if the path is valid before setting
                if (File.Exists(executablePath))
                {
                    key.SetValue(appName, executablePath);
                }
                else
                {
                    throw new FileNotFoundException("Application executable not found");
                }
            }
            else
            {
                if (key.GetValue(appName) != null)
                {
                    key.DeleteValue(appName, false);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox messageBox = new()
            {
                Title = "Error",
                Content = $"Failed to set startup: {ex.Message}",
                CloseButtonText = "OK",
            };

            _ = messageBox.ShowDialogAsync();
        }
    }

    private void StartupHyperlink_RequestNavigate(object sender,
        System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
    
    private async void UnlockPremiumButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Disable button during purchase
            if (sender is System.Windows.Controls.Button button)
            {
                button.IsEnabled = false;
                button.Content = "Processing...";
            }

            bool success = await LicenseManager.Instance.PurchasePremiumAsync();
            
            if (success)
            {
                // Update settings with new license status
                SettingsManager.Current.IsPremiumUnlocked = true;

                MessageBox messageBox = new()
                {
                    Title = "Success",
                    Content = FindResource("PremiumPurchaseSuccess").ToString(),
                    CloseButtonText = "OK",
                };

                await messageBox.ShowDialogAsync();
            }
            else
            {
                MessageBox messageBox = new()
                {
                    Title = "Purchase Failed",
                    Content = FindResource("PremiumPurchaseFailed").ToString(),
                    CloseButtonText = "OK",
                };

                await messageBox.ShowDialogAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox messageBox = new()
            {
                Title = "Error",
                Content = $"An error occurred: {ex.Message}",
                CloseButtonText = "OK",
            };

            await messageBox.ShowDialogAsync();
        }
        finally
        {
            // Re-enable button
            if (sender is System.Windows.Controls.Button button)
            {
                button.IsEnabled = true;
                button.Content = FindResource("UnlockPremiumButton").ToString();
            }
        }
    }

    private void ToggleSwitch_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = (bool)NIconHideSwitch.IsChecked;

        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;

        if (!isChecked)
        {
            mainWindow.nIcon.Register();
        }
        else
        {
            mainWindow.nIcon.Unregister();
        }
    }
}
