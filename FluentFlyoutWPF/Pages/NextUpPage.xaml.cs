using FluentFlyout.Classes.Settings;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class NextUpPage : Page
{
    public NextUpPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }
}
