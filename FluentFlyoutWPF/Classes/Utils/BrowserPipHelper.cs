// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.Text;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyout.Classes.Utils;

/// <summary>
/// Works with the browser's picture-in-picture window.
/// <para>
/// Closing it from outside is possible: the small window is an ordinary top level window of the browser, so it
/// accepts <c>WM_CLOSE</c>. Measured on Chromium, though, a window closed that way makes the browser pause the
/// media (it treats the window as its video being taken away), so it is the last resort and never the first
/// choice; leaving picture-in-picture through the page API (what the companion extension does) keeps playing.
/// It is deliberately never hidden or shown: hiding it takes it away from the browser, and a browser window
/// that was hidden and shown by another program loses its connection to the page, which leaves a floating
/// window whose buttons do nothing.
/// </para>
/// <para>
/// Opening it cannot be done from outside the browser at all: Chromium offers no command line switch, window
/// message or COM interface for it, and any page (YouTube, Bilibili, ...) replaces the browser's own context
/// menu with its own, so simulating a user cannot reach the entry either. That is what the companion extension
/// is for: it registers the system wide shortcut <see cref="PipHotkeyText"/> and toggles picture-in-picture
/// through the page API, and this helper only presses that shortcut.
/// </para>
/// </summary>
public static class BrowserPipHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// The shortcut the companion browser extension registers for toggling picture-in-picture. It is the one
    /// bound to the extension's toolbar button (<c>_execute_action</c>), because that path goes through the
    /// browser's own user interface, which is what Chromium wants before it lets a video enter
    /// picture-in-picture. Both shortcuts have to be marked global in the extension, otherwise they stop
    /// working as soon as the browser loses focus; see the extension's README before changing this.
    /// </summary>
    public const string PipHotkeyText = "Ctrl+Shift+6";

    private const string BrowserWindowClass = "Chrome_WidgetWin_1";

    // the titles Chromium gives its picture-in-picture window in the languages that are likely around here; a
    // browser window is only ever closed when its title is one of these, so no other window can be hit
    private static readonly string[] PipWindowTitles =
    [
        "画中画", "子母畫面", "Picture in picture", "Picture-in-Picture", "Picture-in-picture",
        "ピクチャーインピクチャー", "Bild-in-Bild", "Imagen en imagen", "Image dans l'image",
        "Картинка в картинке", "Immagine nell'immagine", "Picture-in-picture (PiP)"
    ];

    /// <summary>
    /// Finds the picture-in-picture window of a browser, or <see cref="IntPtr.Zero"/> when there is none.
    /// </summary>
    public static IntPtr FindPipWindow()
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((window, _) =>
        {
            if (found != IntPtr.Zero || !IsWindowVisible(window))
                return true;

            if (GetClassNameText(window) != BrowserWindowClass)
                return true;

            string title = GetWindowTitle(window);

            if (!PipWindowTitles.Any(candidate => title.Trim().Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (!IsBrowserProcess(window))
                return true;

            found = window;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// Whether a picture-in-picture window still exists.
    /// </summary>
    public static bool IsOpen(IntPtr window) => window != IntPtr.Zero && IsWindow(window);

    /// <summary>
    /// Gets the process a window belongs to, or null when it cannot be read.
    /// </summary>
    public static int? GetOwningProcessId(IntPtr window)
    {
        if (!IsOpen(window))
            return null;

        try
        {
            GetWindowProcessId(window, out uint processId);
            return processId == 0 ? null : (int)processId;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to read the process of a window");
            return null;
        }
    }

    /// <summary>
    /// Asks a picture-in-picture window to close, exactly like its own close button would.
    /// </summary>
    public static void ClosePipWindow(IntPtr window)
    {
        if (!IsOpen(window))
            return;

        Logger.Info("Closing the browser picture-in-picture window {0}", GetWindowTitle(window));
        PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Presses the companion extension's shortcut, which toggles picture-in-picture for the video that is
    /// playing in the browser. The shortcut is system wide, so the browser stays in the background.
    /// </summary>
    public static void SendPipToggleHotkey()
    {
        Logger.Info("Sending {0} to toggle the browser picture-in-picture window", PipHotkeyText);

        keybd_event(VK_CONTROL, 0, 0, IntPtr.Zero);
        keybd_event(VK_SHIFT, 0, 0, IntPtr.Zero);
        keybd_event(VK_6, 0, 0, IntPtr.Zero);
        keybd_event(VK_6, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
    }

    /// <summary>
    /// Whether a media session belongs to a browser.
    /// </summary>
    public static bool IsBrowserSession(string? sessionId) => MediaPlayerData.IsBrowser(sessionId);

    private static string GetClassNameText(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        GetClassName(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetWindowTitle(IntPtr window)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static bool IsBrowserProcess(IntPtr window)
    {
        try
        {
            GetWindowProcessId(window, out uint processId);

            using var process = Process.GetProcessById((int)processId);
            return MediaPlayerData.IsBrowser(process.ProcessName);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to check the owner of a picture-in-picture window");
            return false;
        }
    }
}
