using FluentFlyout.Classes.Settings;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Wpf.Ui.Appearance;

namespace FluentFlyout.Classes;

internal enum AccentState
{
    ACCENT_DISABLED = 0,
    ACCENT_ENABLE_GRADIENT = 1,
    ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
    ACCENT_ENABLE_BLURBEHIND = 3,
    ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
    ACCENT_INVALID_STATE = 5
}

[StructLayout(LayoutKind.Sequential)]
internal struct AccentPolicy
{
    public AccentState AccentState;
    public uint AccentFlags;
    public uint GradientColor;
    public uint AnimationId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowCompositionAttributeData
{
    public WindowCompositionAttribute Attribute;
    public IntPtr Data;
    public int SizeOfData;
}

internal enum WindowCompositionAttribute
{
    WCA_ACCENT_POLICY = 19
}

public static class WindowBlurHelper
{
    [DllImport("user32.dll")]
    internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// Enables acrylic blur effect on the specified window
    /// </summary>
    /// <param name="window">The window to apply blur to</param>
    /// <param name="blurOpacity">Opacity of the blur (0-255)</param>
    /// <param name="blurBackgroundColor">Background color in BGR format (default: 0x000000)</param>
    public static void EnableBlur(Window window, uint blurOpacity = 175, uint blurBackgroundColor = 0x000000)
    {
        // override opacity if premium is unlocked
        if (SettingsManager.Current.IsPremiumUnlocked) blurOpacity = SettingsManager.Current.AcrylicBlurOpacity;
        blurOpacity = Math.Clamp(blurOpacity, 0, 255);

        var windowHelper = new WindowInteropHelper(window);

        var currentTheme = ApplicationThemeManager.GetAppTheme();
        if (currentTheme == ApplicationTheme.Light)
        {
            blurBackgroundColor = 0xFFFFFF; // use light background for light theme
        }

        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            GradientColor = (blurOpacity << 24) | (blurBackgroundColor & 0xFFFFFF)
        };

        var accentStructSize = Marshal.SizeOf(accent);
        var accentPtr = Marshal.AllocHGlobal(accentStructSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };

        SetWindowCompositionAttribute(windowHelper.Handle, ref data);

        Marshal.FreeHGlobal(accentPtr);
    }

    /// <summary>
    /// Disables blur effect on the specified window
    /// </summary>
    /// <param name="window">The window to disable blur on</param>
    public static void DisableBlur(Window window)
    {
        var windowHelper = new WindowInteropHelper(window);

        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_DISABLED
        };

        var accentStructSize = Marshal.SizeOf(accent);
        var accentPtr = Marshal.AllocHGlobal(accentStructSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };

        SetWindowCompositionAttribute(windowHelper.Handle, ref data);

        Marshal.FreeHGlobal(accentPtr);
    }

    /// <summary>
    /// Adjusts the blur opacity for all windows that have acrylic blur enabled
    /// </summary>
    /// <param name="newBlurOpacity">New opacity value (0-255)</param>
    public static void AdjustBlurOpacityForAllWindows(uint newBlurOpacity)
    {
        if (!SettingsManager.Current.IsPremiumUnlocked) return;
        newBlurOpacity = Math.Clamp(newBlurOpacity, 0, 255);

        foreach (Window window in Application.Current.Windows)
        {
            if (window == null) continue;

            var windowHelper = new WindowInteropHelper(window);
            if (windowHelper.Handle == IntPtr.Zero) continue;

            // check if window should have acrylic blur based on settings
            if (ShouldHaveAcrylicBlur(window))
            {
                var currentTheme = ApplicationThemeManager.GetAppTheme();
                uint blurBackgroundColor = currentTheme == ApplicationTheme.Light ? 0xFFFFFFu : 0x000000u;

                var accent = new AccentPolicy
                {
                    AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    GradientColor = (newBlurOpacity << 24) | (blurBackgroundColor & 0xFFFFFF)
                };

                var accentStructSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = accentStructSize,
                    Data = accentPtr
                };

                SetWindowCompositionAttribute(windowHelper.Handle, ref data);

                Marshal.FreeHGlobal(accentPtr);
            }
        }
    }

    /// <summary>
    /// Checks if a window should have acrylic blur enabled based on settings
    /// </summary>
    private static bool ShouldHaveAcrylicBlur(Window window)
    {
        var windowType = window.GetType().Name;

        return windowType switch
        {
            "MainWindow" => SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled,
            "NextUpWindow" => SettingsManager.Current.NextUpAcrylicWindowEnabled,
            "LockWindow" => SettingsManager.Current.LockKeysAcrylicWindowEnabled,
            _ => false
        };
    }
}
