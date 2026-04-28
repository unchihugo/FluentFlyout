// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF.Classes;

public static class WindowHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;

        public static implicit operator Rect(RECT rect)
        {
            return new Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
        }
    }

    public static void SetTopmost(Window window) // workaround to set window even more topmost
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static void SetVisibility(Window window, bool visible) // workaround to set window even more topmost
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, 0, 0, 0, 0, 0, (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW)));
    }

    public static Rect GetPlacement(Window window) // get the window position in screen coordinates, ignoring WPF
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (GetWindowRect(handle, out NativeMethods.RECT rect))
        {
            return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        
        // Fallback (should not happen for valid windows)
        return new Rect(window.Left, window.Top, window.Width, window.Height);
    }

    public static void SetPosition(Window window, double x, double y, bool async = false) // set the position of the window, ignoring WPF
    {
        var handle = new WindowInteropHelper(window).Handle;
        uint flags = SWP_NOSIZE | SWP_NOZORDER | (async ? SWP_ASYNCWINDOWPOS : (uint)0);
        bool result = SetWindowPos(handle, 0, (int)x, (int)y, 0, 0, flags);

        if (!result)
        {
            int error = Marshal.GetLastWin32Error();
            Logger.Warn($"SetPosition failed for '{window.GetType().Name}' (HWND=0x{handle.ToInt64():X}, X={x}, Y={y}, Flags=0x{flags:X}), Win32Error={error}");
        }

        return;
    }

    public static void SetPositionAndSize(Window window, double x, double y, double width, double height, bool async = false) // set the position and size of the window, ignoring WPF
    {
        var handle = new WindowInteropHelper(window).Handle;
        uint flags = SWP_NOZORDER | (async ? SWP_ASYNCWINDOWPOS : (uint)0);
        bool result = SetWindowPos(handle, 0, (int)x, (int)y, (int)width, (int)height, flags);

        if (!result)
        {
            int error = Marshal.GetLastWin32Error();
            Logger.Warn($"SetPositionAndSize failed for '{window.GetType().Name}' (HWND=0x{handle.ToInt64():X}, X={x}, Y={y}, W={width}, H={height}, Flags=0x{flags:X}), Win32Error={error}");
        }

        return;
    }

    public static void SetNoActivate(Window window) // prevent window from stealing focus
    {
        window.SourceInitialized += (sender, e) =>
        {
            var helper = new WindowInteropHelper(window);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        };
    }

    // Check if the mouse cursor is currently over the specified window
    // More reliable than WPF's IsMouseOver, it sometimes doesn't detect mouse over the background
    public static bool IsMouseOverWindow(Window window)
    {
        if (!GetCursorPos(out POINT cursor))
            return false;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(hwnd, out NativeMethods.RECT rect))
            return false;

        return cursor.X >= rect.Left && cursor.X <= rect.Right &&
               cursor.Y >= rect.Top && cursor.Y <= rect.Bottom;
    }
}