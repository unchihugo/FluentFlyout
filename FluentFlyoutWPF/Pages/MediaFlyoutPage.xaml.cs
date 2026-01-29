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
}
