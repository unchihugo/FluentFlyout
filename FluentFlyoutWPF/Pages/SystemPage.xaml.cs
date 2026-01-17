using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace FluentFlyoutWPF.Pages;

public partial class SystemPage : Page
{
    public SystemPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
        UpdateMonitorList();
    }

    private void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        SetStartup(StartupSwitch.IsChecked ?? false);
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

    private void StartupHyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
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

    private void UpdateMonitorList()
    {
        var monitors = WindowHelper.GetMonitors();
        FlyoutSelectedMonitorComboBox.Items.Clear();

        var resetToPrimary = SettingsManager.Current.FlyoutSelectedMonitor >= monitors.Count || 
                           SettingsManager.Current.FlyoutSelectedMonitor < 0;
        int selectedMonitor = SettingsManager.Current.FlyoutSelectedMonitor;

        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var cb = new ComboBoxItem()
            {
                Content = monitor.isPrimary ? (i + 1).ToString() + " *" : (i + 1).ToString(),
            };
            if (resetToPrimary && monitor.isPrimary)
                selectedMonitor = i;

            FlyoutSelectedMonitorComboBox.Items.Add(cb);
        }

        FlyoutSelectedMonitorComboBox.SelectedIndex = selectedMonitor;
        SettingsManager.Current.FlyoutSelectedMonitor = selectedMonitor;
    }
}
