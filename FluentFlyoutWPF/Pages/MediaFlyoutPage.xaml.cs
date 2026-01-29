using FluentFlyout.Classes.Settings;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class MediaFlyoutPage : Page
{
    public MediaFlyoutPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }

    private void CardControl_Click(object sender, System.Windows.RoutedEventArgs e)
    {

    }

    private void CardControl_Click_1(object sender, System.Windows.RoutedEventArgs e)
    {

    }
}
