using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using System.Windows;
using System.Windows.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace FluentFlyoutWPF.Pages;

public partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
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
}
