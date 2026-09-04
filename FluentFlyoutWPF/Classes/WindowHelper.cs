// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static FluentFlyoutWPF.Classes.NativeMethods;

namespace FluentFlyoutWPF.Classes;

public static class WindowHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static void SetTopmost(Window window) // workaround to set window even more topmost
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static void SetVisibility(Window window, bool visible) // workaround to set window even more topmost
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, 0, 0, 0, 0, 0, (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW)));
    }

    public static Rect GetPlacement(Window window) // get the window position, ignoring WPF
    {
        var handle = new WindowInteropHelper(window).Handle;
        GetWindowRect(handle, out RECT windowRect);
        return new Rect(
            windowRect.Left,
            windowRect.Top,
            windowRect.Right - windowRect.Left,
            windowRect.Bottom - windowRect.Top);
    }

    public static void SetPosition(Window window, double x, double y, bool async = false) // set the position of the window, ignoring WPF
    {
        var handle = new WindowInteropHelper(window).Handle;
        uint flags = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | (async ? SWP_ASYNCWINDOWPOS : (uint)0);
        bool result = SetWindowPos(handle, 0, (int)x, (int)y, 0, 0, flags);

        if (!result)
        {
            int error = Marshal.GetLastWin32Error();
            Logger.Warn($"SetPosition failed for '{window.GetType().Name}' (HWND=0x{handle.ToInt64():X}, X={x}, Y={y}, Flags=0x{flags:X}), Win32Error={error}");
        }

        return;
    }

    public static void SetNoActivate(Window window) // prevent window from stealing focus
    {
        window.ShowActivated = false;

        void ApplyNoActivateStyle()
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero)
                return;

            SetWindowLong(helper.Handle, GWL_EXSTYLE, GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        }

        window.SourceInitialized += (sender, e) => ApplyNoActivateStyle();
        ApplyNoActivateStyle();
    }

    // Check if the mouse cursor is currently over the specified window
    // More reliable than WPF's IsMouseOver, it sometimes doesn't detect mouse over the background
    public static bool IsMouseOverWindow(Window window)
    {
        if (!GetCursorPos(out POINT cursor))
            return false;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(hwnd, out RECT rect))
            return false;

        return cursor.X >= rect.Left && cursor.X <= rect.Right &&
               cursor.Y >= rect.Top && cursor.Y <= rect.Bottom;
    }
}