// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.ComponentModel;
using System.Diagnostics;

namespace FluentFlyout.Classes.Utils;

/// <summary>
/// Launches media applications through their registered URI protocol handler.
/// </summary>
internal static class MediaAppLauncher
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // ShellExecuteEx error expected when no handler is registered (unverified, confirm on a machine without Spotify)
    private const int ERROR_NO_ASSOCIATION = 1155;

    // session ids: the desktop installer reports the executable name, the Store package reports its AppUserModelId
    // (unverified on a live system, the session id is logged on detection so these can be corrected)
    internal static readonly MediaAppDefinition Spotify = new("Spotify", "spotify:", ["Spotify.exe", "SpotifyAB.SpotifyMusic_"]);

    /// <summary>
    /// Asks the Windows shell to open the app. Success means the request was handed to the
    /// registered handler, not that the app window is visible or focused.
    /// </summary>
    public static MediaAppLaunchResult TryLaunch(MediaAppDefinition app)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = app.LaunchUri,
                UseShellExecute = true
            })?.Dispose();

            Logger.Info("Requested launch of {0} via {1}", app.DisplayName, app.LaunchUri);
            return MediaAppLaunchResult.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_NO_ASSOCIATION)
        {
            Logger.Warn(ex, "No handler registered for {0} (native error {1})", app.LaunchUri, ex.NativeErrorCode);
            return MediaAppLaunchResult.NotInstalled;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to launch {0} via {1}", app.DisplayName, app.LaunchUri);
            return MediaAppLaunchResult.Failed;
        }
    }
}