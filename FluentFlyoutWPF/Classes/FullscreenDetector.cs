// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF.Classes;

internal class FullscreenDetector
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Checks if a DirectX exclusive fullscreen application or game is currently running.
    /// </summary>
    /// <returns>
    /// true if a fullscreen DirectX application is running;
    /// false if no fullscreen application is detected, DisableIfFullscreen setting is false, or if the check fails
    /// </returns>
    public static bool IsFullscreenApplicationRunning()
    {
        if (!SettingsManager.Current.DisableIfFullscreen) return false;
        try
        {
            QUERY_USER_NOTIFICATION_STATE state;
            int result = SHQueryUserNotificationState(out state);

            if (result != 0) // 0 means SUCCESS
            {
                throw new Exception($"SHQueryUserNotificationState failed with error code: {result}");
            }

            return state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error detecting fullscreen state");
            return false;
        }
    }
}