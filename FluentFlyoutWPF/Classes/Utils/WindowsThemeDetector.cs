using System;
using Microsoft.Win32;

// Custom Theme detector from checking the Windows Registry.
// WindowsThemeHelper.GetCurrentWindowsTheme and MicaWPFServiceUtility.ThemeService.CurrentTheme will return the wrong value
// for custom themes (ie. Windows mode = dark and App mode = light and vice versa).
// If for some reason the registry read fails or somehow explodes, default to light...

// Usage:
// WindowsThemeDetector.GetWindowsTheme(out var appTheme, out var systemTheme);
// Console.WriteLine($"App Theme: {appTheme}");
// Console.WriteLine($"System Theme: {systemTheme}");

public class WindowsThemeDetector
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppThemeValueName = "AppsUseLightTheme";
    private const string SystemThemeValueName = "SystemUsesLightTheme";

    public enum ThemeMode
    {
        Light,
        Dark,
        Unknown
    }

    /// <summary>
    /// Gets the current theme for Applications and the System.
    /// </summary>
    public static void GetWindowsTheme(out ThemeMode appTheme, out ThemeMode systemTheme)
    {
        appTheme = GetThemeFromRegistry(AppThemeValueName);
        systemTheme = GetThemeFromRegistry(SystemThemeValueName);
    }

    private static ThemeMode GetThemeFromRegistry(string valueName)
    {
        try
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                object? registryValue = key?.GetValue(valueName);

                if (registryValue == null)
                    // On error, default to light
                    return ThemeMode.Light;

                // 1 means Light Mode, 0 means Dark Mode
                return (int)registryValue > 0 ? ThemeMode.Light : ThemeMode.Dark;
            }
        }
        catch (Exception)
        {
            // On error, default to light
            return ThemeMode.Light;
        }
    }
}