using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.ViewModels;
using System.Windows;
using System.Windows.Controls;
namespace FluentFlyoutWPF.Pages;

public partial class AboutPage : Page
{
    public AboutViewModel AboutViewModel { get; } = new();
    public UserSettings UserSettings => SettingsManager.Current;

    public AboutPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    // same as in HomePage.xaml.cs
    private async void UnlockPremiumButton_Click(object sender, RoutedEventArgs e)
    {
        LicenseManager.UnlockPremium(sender);
    }
}
