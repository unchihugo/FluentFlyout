using FluentFlyout.Classes.Settings;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class LockKeysPage : Page
{
    public LockKeysPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }
}
