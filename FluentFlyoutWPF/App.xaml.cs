using FluentFlyout.Classes;
using System.Windows;

namespace FluentFlyoutWPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Apply localization before any windows are created
        LocalizationManager.ApplyLocalization();
        
        base.OnStartup(e);
    }
}