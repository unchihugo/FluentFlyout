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

    public struct MonitorInfo
    {
        public Rect monitorArea;
        public Rect workArea;
        public bool isPrimary;
        public uint dpiX;
        public uint dpiY;
        public string deviceId;
    }

    public static void SetTopmost(Window window) // workaround to set window even more topmost
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    }

    public static void SetVisibility(Window window, bool visible) // workaround to set window even more topmost
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, 0, 0, 0, 0, 0, (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW)));
    }

    public static Rect GetPlacement(Window window) // get the window position, ignoring WPF
    {
        var wp = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };

        var handle = new WindowInteropHelper(window).Handle;
        GetWindowPlacement(handle, ref wp);

        return new Rect(wp.rcNormalPosition.Left, wp.rcNormalPosition.Top, 
            wp.rcNormalPosition.Right - wp.rcNormalPosition.Left, 
            wp.rcNormalPosition.Bottom - wp.rcNormalPosition.Top);
    }

    public static void SetPosition(Window window, double x, double y, bool async = false) // set the position of the window, ignoring WPF
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, 0, (int)x, (int)y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | (async ? SWP_ASYNCWINDOWPOS : (uint)0));

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

    private static MonitorInfo getMonitorInfoInternal(IntPtr hMonitor)
    {
        var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };

        if (GetMonitorInfo(hMonitor, ref info))
        {
            MonitorInfo new_info = new MonitorInfo
            {
                monitorArea = new Rect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top),
                workArea = new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top),
                isPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                deviceId = info.szDevice
            };

            if (GetDpiForMonitor(hMonitor, MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == S_OK)
            {
                new_info.dpiX = dpiX;
                new_info.dpiY = dpiY;
            }
            else
            {
                new_info.dpiX = 96;
                new_info.dpiY = 96;
            }

            return new_info;
        }

        return new MonitorInfo();
    }

    public static MonitorInfo GetMonitor(IntPtr hwnd, MonitorFromWindowFlags flag = MonitorFromWindowFlags.DEFAULTTONEAREST)
    {
        var hMonitor = MonitorFromWindow(hwnd, (int)flag);
        return getMonitorInfoInternal(hMonitor);
    }

    public static MonitorInfo GetMonitor(Window window, MonitorFromWindowFlags flag = MonitorFromWindowFlags.DEFAULTTONEAREST)
    {
        return GetMonitor(new WindowInteropHelper(window).Handle, flag);
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        List<MonitorInfo> result = [];

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, _, ref _, _) =>
            {
                result.Add(getMonitorInfoInternal(hMonitor));
                return true;
            },
            IntPtr.Zero
        );

        return result;
    }

}