using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class TaskbarVisualizerPage : Page
{
    public TaskbarVisualizerPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }

    private async void UnlockPremiumButton_Click(object sender, RoutedEventArgs e)
    {
        LicenseManager.UnlockPremium(sender);
    }
}
