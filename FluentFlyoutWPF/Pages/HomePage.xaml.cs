using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using NLog;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using Windows.ApplicationModel;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
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
    }

    private void ViewUpdates_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Notifications.OpenChangelogInBrowser();
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
            string logFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluentFlyout");
            Process.Start("explorer.exe", logFolderPath);
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