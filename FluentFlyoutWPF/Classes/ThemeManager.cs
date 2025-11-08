using System.Windows;
using System.Windows.Media.Imaging;
using FluentFlyout.Classes.Settings;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Helpers;
using MicaWPF.Core.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Tray.Controls;

namespace FluentFlyout.Classes;

/// <summary>
/// Manages the application theme settings and applies the selected theme.
/// </summary>
internal static class ThemeManager
{
    /// <summary>
    /// Applies the theme saved in the application settings. Used at application startup.
    /// </summary>
    /// <inheritdoc cref="ApplyTheme"/>
    public static void ApplySavedTheme()
    {
        ApplyTheme(SettingsManager.Current.AppTheme);
        UpdateTrayIcon();
    }

    /// <summary>
    /// Applies the specified theme and saves it to the application settings.
    /// </summary>
    /// <inheritdoc cref="ApplyTheme"/>
    public static void ApplyAndSaveTheme(int theme)
    {
        ApplyTheme(theme);
        SettingsManager.Current.AppTheme = theme;
        SettingsManager.SaveSettings();
    }

    /// <summary>
    /// Applies the specified theme. See also <see href="https://github.com/Simnico99/MicaWPF/wiki/Change-Theme-or-Accent-color"/>.
    /// </summary>
    /// <param name="theme">The theme to apply. 1 for Light, 2 for Dark, 0 or any other value for System Default.</param>
    private static void ApplyTheme(int theme)
    {
        switch (theme)
        {
            case 1:
                UnWatchThemeChanges();
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                MicaWPFServiceUtility.ThemeService.ChangeTheme(WindowsTheme.Light);
                break;
            case 2:
                UnWatchThemeChanges();
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                MicaWPFServiceUtility.ThemeService.ChangeTheme(WindowsTheme.Dark);
                break;
            default:
                WatchThemeChanges();
                ApplicationThemeManager.ApplySystemTheme();
                MicaWPFServiceUtility.ThemeService.ChangeTheme(/*WindowsTheme.Auto*/);
                break;
        }

        // refresh accent color to its counterpart after theme changes
        MicaWPFServiceUtility.AccentColorService.RefreshAccentsColors();
    }

    /// <summary>
    /// Starts watching for system theme changes and applies them automatically. (just a wrapper for <see cref="SystemThemeWatcher.Watch"/>)
    /// </summary>
    /// <remarks>This function was not necessary because the theme was managed by MicaWPF.</remarks>
    private static void WatchThemeChanges()
    {
        SystemThemeWatcher.Watch(Application.Current.MainWindow/*, WindowBackdropType.Mica, true*/);
    }

    /// <summary>
    /// Stops watching for system theme changes. (just a wrapper for <see cref="SystemThemeWatcher.UnWatch"/>)
    /// </summary>
    /// <remarks>This function was not necessary because the theme was managed by MicaWPF.</remarks>
    private static void UnWatchThemeChanges()
    {
        SystemThemeWatcher.UnWatch(Application.Current.MainWindow);
    }

    /// <summary>
    /// Changes the tray icon according to the specified app theme and setting.
    /// </summary>
    public static void UpdateTrayIcon()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (Application.Current.MainWindow.FindName("nIcon") is NotifyIcon nIcon)
            {
                if (SettingsManager.Current.NIconSymbol == true)
                {
                    var iconUri = new Uri(WindowsThemeHelper.GetCurrentWindowsTheme() == WindowsTheme.Dark
                        ? "pack://application:,,,/Resources/TrayIcons/FluentFlyoutWhite.png"
                        : "pack://application:,,,/Resources/TrayIcons/FluentFlyoutBlack.png");
                    nIcon.Icon = new BitmapImage(iconUri);
                }
                else
                {
                    var iconUi = new Uri("pack://application:,,,/Resources/FluentFlyout2.ico");
                    nIcon.Icon = new BitmapImage(iconUi);
                }
            }
        });
    }
}