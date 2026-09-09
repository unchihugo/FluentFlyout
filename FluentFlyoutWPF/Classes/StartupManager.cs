// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace FluentFlyoutWPF.Classes;

/// <summary>
/// Owns the operating-system startup registration for both packaged and
/// unpackaged builds. Callers change the persisted UserSettings value only
/// after this component has reported whether the OS accepted the change.
/// </summary>
internal static class StartupManager
{
    private const string StartupTaskId = "FluentFlyoutStartup";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "FluentFlyout";

    private static readonly SemaphoreSlim OperationLock = new(1, 1);
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    internal readonly record struct StartupOperationResult(bool Success, bool? IsEnabled, string? Error);

    /// <summary>
    /// Applies the requested startup state using exactly one OS mechanism.
    /// </summary>
    internal static async Task<StartupOperationResult> ApplyAsync(bool enable)
    {
        // StartupTask.RequestEnableAsync must run on the UI thread. All current
        // callers are UI event/startup paths, so retain their context while the
        // serialized operation waits.
        await OperationLock.WaitAsync();
        try
        {
            if (IsPackaged())
                return await ApplyPackagedAsync(enable);

            return ApplyUnpackaged(enable);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to apply startup setting");
            return new StartupOperationResult(false, TryGetRunEntryEnabled(), ex.Message);
        }
        finally
        {
            OperationLock.Release();
        }
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Package.Current.Id;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // Package.Current has no identity for an unpackaged executable.
            return false;
        }
    }

    private static async Task<StartupOperationResult> ApplyPackagedAsync(bool enable)
    {
        // A previous unpackaged build may have left a Run value behind. Remove
        // it before touching StartupTask so the two mechanisms can never launch
        // the packaged app together.
        if (!RemoveLegacyRunEntry(out string? cleanupError))
        {
            Logger.Error("Could not remove the legacy Run startup entry: {0}", cleanupError);
            return new StartupOperationResult(false, null, cleanupError);
        }

        StartupTask startupTask;
        try
        {
            startupTask = await StartupTask.GetAsync(StartupTaskId);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not retrieve packaged startup task {0}", StartupTaskId);
            return new StartupOperationResult(false, null, ex.Message);
        }

        try
        {
            if (enable)
            {
                StartupTaskState state = startupTask.State;
                if (!IsEnabled(state))
                    state = await startupTask.RequestEnableAsync();

                bool isEnabled = IsEnabled(state);
                return isEnabled
                    ? new StartupOperationResult(true, true, null)
                    : new StartupOperationResult(false, false, $"Windows returned startup state {state}.");
            }

            if (IsEnabled(startupTask.State))
                startupTask.Disable();

            bool isStillEnabled = IsEnabled(startupTask.State);
            return isStillEnabled
                ? new StartupOperationResult(false, true, $"Windows kept startup task enabled ({startupTask.State}).")
                : new StartupOperationResult(true, false, null);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not {0} packaged startup task", enable ? "enable" : "disable");
            return new StartupOperationResult(false, IsEnabled(startupTask.State), ex.Message);
        }
    }

    private static bool IsEnabled(StartupTaskState state)
    {
        return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    private static StartupOperationResult ApplyUnpackaged(bool enable)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("The Windows startup Run key could not be opened.");

            if (enable)
            {
                string executablePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("The application executable path is unavailable.");
                if (!File.Exists(executablePath))
                    throw new FileNotFoundException("The application executable was not found.", executablePath);

                string startupCommand = $"\"{executablePath}\"";
                key.SetValue(RunValueName, startupCommand, RegistryValueKind.String);

                bool verified = string.Equals(
                    key.GetValue(RunValueName) as string,
                    startupCommand,
                    StringComparison.OrdinalIgnoreCase);
                return verified
                    ? new StartupOperationResult(true, true, null)
                    : new StartupOperationResult(false, false, "The Windows startup Run value could not be verified.");
            }

            if (key.GetValue(RunValueName) != null)
                key.DeleteValue(RunValueName, throwOnMissingValue: false);

            bool removed = key.GetValue(RunValueName) == null;
            return removed
                ? new StartupOperationResult(true, false, null)
                : new StartupOperationResult(false, true, "The Windows startup Run value could not be removed.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not {0} unpackaged startup", enable ? "enable" : "disable");
            return new StartupOperationResult(false, TryGetRunEntryEnabled(), ex.Message);
        }
    }

    private static bool RemoveLegacyRunEntry(out string? error)
    {
        error = null;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null || key.GetValue(RunValueName) == null)
                return true;

            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            if (key.GetValue(RunValueName) != null)
            {
                error = "The legacy Windows startup Run value is still present.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool? TryGetRunEntryEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) != null;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not verify the Windows startup Run value");
            return null;
        }
    }
}
