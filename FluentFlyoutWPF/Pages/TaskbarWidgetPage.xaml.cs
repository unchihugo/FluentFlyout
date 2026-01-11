using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using System.Windows;
using System.Windows.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;

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
        try
        {
            if (sender is Button button)
            {
                button.IsEnabled = false;
                button.Content = "Processing...";
            }

            (bool success, string result) = await LicenseManager.Instance.PurchasePremiumAsync();

            if (success)
            {
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
                    Content = $"{FindResource("PremiumPurchaseFailed")} ({result})",
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
            if (sender is Button button)
            {
                button.IsEnabled = true;
                button.Content = FindResource("UnlockPremiumButton").ToString();
            }
        }
    }

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
