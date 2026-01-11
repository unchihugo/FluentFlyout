using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using System.Windows.Controls;
using Windows.ApplicationModel;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class HomePage : Page
{
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
}
