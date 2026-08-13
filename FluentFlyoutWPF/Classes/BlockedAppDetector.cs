// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using System.Diagnostics;

namespace FluentFlyoutWPF.Classes;

public static class BlockedAppDetector
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static bool IsBlockedAppInForeground()
    {
        // Get the handle of the foreground window
        IntPtr foregroundWindowHandle = NativeMethods.GetForegroundWindow();
        
        // Get the process ID of the foreground window
        NativeMethods.GetWindowThreadProcessId(foregroundWindowHandle, out uint processId);
        
        // Get the process name from the process ID
        string processName;
        try
        {
            processName = Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException)
        {
            Logger.Debug("Process with ID {0} not found. It may have exited.", processId);
            return false;
        }
        
        // Check if the process name is in the blocked apps list
        return SettingsManager.Current.BlockedApps.Contains(processName, StringComparer.OrdinalIgnoreCase);
    }
}