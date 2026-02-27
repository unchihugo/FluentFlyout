// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF.Classes.Utils;

public static class MonitorUtil
{
    public struct MonitorInfo
    {
        public Rect monitorArea;
        public Rect workArea;
        public bool isPrimary;
        public uint dpiX;
        public uint dpiY;
        public string deviceId;
        public string deviceName;
    }


    public static MonitorInfo getSelectedMonitor(int index = 0)
    {
        var monitors = GetMonitors();
        return monitors[Math.Clamp(index, 0, monitors.Count - 1)];
    }

    private static MonitorInfo getMonitorInfoInternal(IntPtr hMonitor)
    {
        var info = new NativeMethods.MONITORINFOEX();
        info.cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>();
        
        if(GetMonitorInfo(hMonitor, ref info))
        {
            MonitorInfo new_info = new MonitorInfo
            {
                monitorArea = new Rect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top),
                workArea = new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top),
                isPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                deviceId = info.szDevice,
                deviceName = GetMonitorFriendlyName(info.szDevice)
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

        return new MonitorInfo();  // Defaults will be empty/null strings for new fields
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


        return result
            .OrderByDescending(m => m.isPrimary)
            .ThenBy(m => m.monitorArea.Left)
            .ToList();
    }
    private static string GetMonitorFriendlyName(string deviceId)
    {
        var displayDevice = new NativeMethods.DISPLAY_DEVICE
        {
            cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>()
        };

        //Enumurate all display devices to find display given by deviceId
        if (EnumDisplayDevices(deviceId, 0, ref displayDevice, 0))
        {
            return displayDevice.DeviceString.Trim(); // "Eg: Dell U2720Q"
        }
        return "Unknown Monitor";
    }

    public static void UpdateMonitorList(ComboBox comboBox, Func<int> getSelectedIndex, Action<int> setSelectedIndex)
    {
        var monitors = GetMonitors();
        comboBox.Items.Clear();

        int savedIndex = getSelectedIndex();

        bool resetToPrimary =
            savedIndex >= monitors.Count ||
            savedIndex < 0;

        int selectedMonitor = savedIndex;

        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];

            string name = $"{i + 1} ({monitor.deviceName})";
            string contentName = monitor.isPrimary ? $"* {name}" : $"{name}";

            var cb = new ComboBoxItem
            {
                Content = contentName
            };

            if (resetToPrimary && monitor.isPrimary)
                selectedMonitor = i;

            comboBox.Items.Add(cb);
        }

        comboBox.SelectedIndex = selectedMonitor;
        setSelectedIndex(selectedMonitor);
    }
}
