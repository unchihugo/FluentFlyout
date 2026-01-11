using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Pages;
using MicaWPF.Controls;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF;

public partial class SettingsWindow : MicaWindow
{
    private static SettingsWindow? instance;
    private Type? _currentPageType;

    public SettingsWindow()
    {
        if (instance != null)
        {
            if (instance.WindowState == WindowState.Minimized)
            {
                instance.WindowState = WindowState.Normal;
            }

            instance.Activate();
            instance.Focus();
            Close();
            return;
        }

        InitializeComponent();
        instance = this;

        Closed += (s, e) => instance = null;
        DataContext = SettingsManager.Current;
    }

    public static void ShowInstance()
    {
        if (instance == null)
        {
            new SettingsWindow().Show();
            instance?.Activate();
        }
        else
        {
            if (instance.WindowState == WindowState.Minimized)
            {
                instance.WindowState = WindowState.Normal;
            }

            instance.Activate();
            instance.Focus();
        }
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _currentPageType = typeof(HomePage);
        RootNavigation.Navigate(_currentPageType);
        
        // Subscribe to navigation to track current page
        RootNavigation.Navigated += (s, args) =>
        {
            _currentPageType = args.Page?.GetType();
        };
        
        // Subscribe to theme change to refresh NavigationView if needed
        SettingsManager.Current.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(SettingsManager.Current.AppTheme))
            {
                // Force NavigationView to refresh after theme change
                Dispatcher.InvokeAsync(() =>
                {
                    if (_currentPageType != null)
                    {
                        Task.Delay(200).Wait();
                        RootNavigation.Navigate(_currentPageType);
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };
    }

    private void SettingsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SettingsManager.SaveSettings();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.SaveSettings();
        Close();
    }
}
