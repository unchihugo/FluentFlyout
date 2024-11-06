using System.Windows;
using FluentFlyout.Properties;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FluentFlyout.Classes;

/// <summary>
/// Manages the application theme settings and applies the selected theme.
/// </summary>
internal static class ThemeManager
{
    /// <summary>
    /// Applies the theme saved in the application settings. Used at application startup.
    /// </summary>
    /// <inheritdoc cref="applyTheme"/>
    public static void ApplySavedTheme()
    {
        applyTheme(Settings.Default.AppTheme);
    }

    /// <summary>
    /// Applies the specified theme and saves it to the application settings.
    /// </summary>
    /// <inheritdoc cref="applyTheme"/>
    public static void ApplyAndSaveTheme(int theme)
    {
        applyTheme(theme);
        Settings.Default.AppTheme = theme;
        Settings.Default.Save();
    }

    /// <summary>
    /// Applies the specified theme. See also <see href="https://github.com/Simnico99/MicaWPF/wiki/Change-Theme-or-Accent-color"/>.
    /// </summary>
    /// <param name="theme">The theme to apply. 1 for Light, 2 for Dark, 0 or any other value for System Default.</param>
    private static void applyTheme(int theme)
    {
        switch (theme)
        {
            case 1:
                unWatchThemeChanges();
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                MicaWPFServiceUtility.ThemeService.ChangeTheme(WindowsTheme.Light);
                break;
            case 2:
                unWatchThemeChanges();
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                MicaWPFServiceUtility.ThemeService.ChangeTheme(WindowsTheme.Dark);
                break;
            default:
                watchThemeChanges();
                ApplicationThemeManager.ApplySystemTheme();
                MicaWPFServiceUtility.ThemeService.ChangeTheme( /*WindowsTheme.Auto*/);
                break;
        }
    }

    /// <summary>
    /// Starts watching for system theme changes and applies them automatically. (just a wrapper for <see cref="SystemThemeWatcher.Watch"/>)
    /// </summary>
    /// <remarks>This function was not necessary because the theme was managed by MicaWPF.</remarks>
    private static void watchThemeChanges()
    {
        SystemThemeWatcher.Watch(Application.Current.MainWindow /*, WindowBackdropType.Mica, true*/);
    }

    /// <summary>
    /// Stops watching for system theme changes. (just a wrapper for <see cref="SystemThemeWatcher.UnWatch"/>)
    /// </summary>
    /// <remarks>This function was not necessary because the theme was managed by MicaWPF.</remarks>
    private static void unWatchThemeChanges()
    {
        SystemThemeWatcher.UnWatch(Application.Current.MainWindow);
    }
}