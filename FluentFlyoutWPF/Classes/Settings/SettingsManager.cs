// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.ViewModels;
using System.IO;
using System.Xml.Serialization;

namespace FluentFlyout.Classes.Settings;

/// <summary>
/// Manages the application settings and saves them to a file in \AppData\FluentFlyout.
/// </summary>
public class SettingsManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly Lock SettingsFileLock = new();

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentFlyout",
        "settings.xml"
    );

    private static UserSettings? _current;
    private static int _saveCounter;
    private static XmlSerializer? _exportSerializer;

    // Replacement work is deliberately tracked independently from the
    // UserSettings debounce CTS. A property-change save may already be
    // replacing settings.xml when shutdown begins, and cancelling the debounce
    // does not cancel that atomic file operation.
    private static readonly object PendingReplacementTasksLock = new();
    private static readonly HashSet<Task> PendingReplacementTasks = [];

    private static XmlSerializer GetExportSerializer()
    {
        if (_exportSerializer == null)
        {
            XmlAttributeOverrides overrides = new XmlAttributeOverrides();
            XmlAttributes ignoreAttrs = new XmlAttributes();
            ignoreAttrs.XmlIgnore = true;
            overrides.Add(typeof(UserSettings), "Uuid", ignoreAttrs);
            overrides.Add(typeof(UserSettings), "IsStoreVersion", ignoreAttrs);
            _exportSerializer = new XmlSerializer(typeof(UserSettings), overrides);
        }
        return _exportSerializer;
    }

    private static bool DeserializeSettings(string filePath, out UserSettings? settings)
    {
        settings = null;

        if (!File.Exists(filePath))
            return false;

        using StreamReader reader = new(filePath);
        XmlSerializer xmlSerializer = new(typeof(UserSettings));
        settings = (UserSettings?)xmlSerializer.Deserialize(reader);
        return settings != null;
    }

    /// <summary>
    /// The current user settings stored in the app.
    /// </summary>
    /// <returns>The current user settings.</returns>
    public static UserSettings Current
    {
        get
        {
            _current ??= new UserSettings();
            return _current;
        }
        set => _current = value;
    }

    /// <summary>
    /// Restores the settings `SettingsManager.Current` from the settings file.
    /// </summary>
    /// <returns>The restored settings.</returns>
    public static UserSettings RestoreSettings(string? filePath = null)
    {
        bool isImport = filePath != null;
        filePath ??= SettingsFilePath;
        string backupPath = filePath + ".bak";

        try
        {
            if (DeserializeSettings(filePath, out var loadedSettings) && loadedSettings != null)
            {
                if (isImport && _current != null)
                {
                    loadedSettings.Uuid = _current.Uuid;
                    loadedSettings.IsStoreVersion = _current.IsStoreVersion;
                }

                _current = loadedSettings;
                _current.CompleteInitialization();

                Logger.Info("Settings successfully restored");
                return _current;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error(ex, "No permission to read in settings file");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error restoring settings");
        }

        // try restoring backup (version before the last save)
        try
        {
            if (DeserializeSettings(backupPath, out var backupSettings) && backupSettings != null)
            {
                _current = backupSettings;
                _current.CompleteInitialization();

                Logger.Warn("Could not restore primary settings file, restored settings from backup");
                return _current;
            }
        }
        catch (Exception backupEx)
        {
            Logger.Error(backupEx, "Error restoring settings from backup file");
        }

        // if the settings/backup file not found or cannot be read
        Logger.Warn("Settings & backup file not found or cannot be read, loading default settings");
        _current = new UserSettings();
        _current.CompleteInitialization();
        return _current;
    }

    /// <summary>
    /// Saves the app settings to the settings file without blocking the caller
    /// on an atomic replacement already in progress.
    /// </summary>
    public static void SaveSettings(string? filePath = null)
    {
        _ = SaveSettingsWithoutThrowingAsync(filePath);
    }

    private static async Task SaveSettingsWithoutThrowingAsync(string? filePath)
    {
        try
        {
            await SaveSettingsAsync(filePath).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error(ex, "No permission to write in settings file");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving settings");
        }
    }

    /// <summary>
    /// Saves settings and completes after an asynchronous atomic replacement has
    /// finished. Use this when the caller must know that the file is on disk,
    /// such as export and shutdown paths. Normal property changes should use
    /// <see cref="SaveSettings(string?)"/> instead.
    /// </summary>
    public static async Task SaveSettingsAsync(string? filePath = null)
    {
        bool isExport = filePath != null;
        filePath ??= SettingsFilePath;
        // Unique temp name per save: the replace step runs asynchronously, so two
        // saves scheduled close together must never share a ".tmp" file. The
        // first one to finish must not delete the temp the second is still about
        // to replace from (#1013, #1072).
        string tempPath = $"{filePath}.{Environment.ProcessId}.{Interlocked.Increment(ref _saveCounter)}.tmp";
        string backupPath = filePath + ".bak";

        Task? replacementTask = null;
        lock (SettingsFileLock)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _current ??= new UserSettings();

            using (var writer = new StreamWriter(tempPath, false))
            {
                XmlSerializer xmlSerializer = isExport
                    ? GetExportSerializer()
                    : new XmlSerializer(typeof(UserSettings));
                xmlSerializer.Serialize(writer, _current);
            }

            if (File.Exists(filePath))
            {
                // Run asynchronously to keep normal in-app property changes
                // non-blocking. The task is tracked separately so shutdown can
                // wait for the actual File.Replace operation.
                replacementTask = Task.Run(async () =>
                {
                    try
                    {
                        await TryReplaceSettingsFileAsync(filePath, tempPath, backupPath);
                        Logger.Info("Settings successfully saved to {0}", filePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error replacing settings file");
                    }
                    finally
                    {
                        TryDeleteFileIfExists(tempPath);
                    }
                });

                TrackPendingReplacementTask(replacementTask);
            }
            else
            {
                try
                {
                    File.Move(tempPath, filePath, true);
                    Logger.Info("Settings successfully saved to {0}", filePath);
                }
                finally
                {
                    TryDeleteFileIfExists(tempPath);
                }
            }
        }

        if (replacementTask != null)
            await replacementTask.ConfigureAwait(false);
    }

    private static void TrackPendingReplacementTask(Task replacementTask)
    {
        lock (PendingReplacementTasksLock)
        {
            PendingReplacementTasks.Add(replacementTask);
        }

        _ = replacementTask.ContinueWith(
            completedTask =>
            {
                lock (PendingReplacementTasksLock)
                {
                    PendingReplacementTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Completes all currently tracked atomic replacements. New replacement work
    /// observed while waiting is included before this method returns.
    /// </summary>
    public static void WaitForPendingSettingsSaves()
    {
        while (true)
        {
            Task[] pendingTasks;
            lock (PendingReplacementTasksLock)
            {
                pendingTasks = [.. PendingReplacementTasks];
            }

            if (pendingTasks.Length == 0)
                return;

            try
            {
                Task.WhenAll(pendingTasks).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Replacement tasks log their own failure, but a fault must not
                // prevent shutdown from observing/removing the remaining tasks.
                Logger.Error(ex, "One or more settings replacements failed while waiting for shutdown");
            }
        }
    }

    private async static Task TryReplaceSettingsFileAsync(string filePath, string tempPath, string backupPath)
    {
        Logger.Debug("Initializing replacing settings file at {0}", filePath);
        int maxAttempts = 5;
        // The following steps try to avoid issues with file locks and permissions on some systems.
        for (int attempts = 1; attempts <= maxAttempts; attempts++)
        {
            try
            {
                // Serialize against other saves: the replace runs outside the
                // caller's lock, so concurrent replaces could interleave and
                // leave settings.xml missing.
                lock (SettingsFileLock)
                {
                    File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
                }
                break;
            }
            catch (IOException ex) when (attempts < maxAttempts)
            {
                // if the file is locked, wait and retry
                Logger.Warn(ex, "Settings file is locked, retrying...");
                await Task.Delay(75);
            }
            catch (IOException ex)
            {
                Logger.Warn(ex, "File.Replace failed after retries, manually replacing...");
                ManualReplace(filePath, tempPath, backupPath);
                return;
            }
        }
    }

    private static void ManualReplace(string filePath, string tempPath, string backupPath)
    {
        lock (SettingsFileLock)
        {
            // Never delete the live settings file before the new content is in
            // place: the old order (delete filePath, then move tempPath) left the
            // user with no settings at all whenever the move failed, so the next
            // start loaded defaults (#1013, #1072). Copy over it instead, which
            // is atomic enough for our purposes and always leaves a readable file.
            if (File.Exists(filePath))
            {
                TryDeleteFileIfExists(backupPath);
                try
                {
                    File.Copy(filePath, backupPath);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Could not refresh settings backup before manual replace");
                }
            }

            File.Copy(tempPath, filePath, true);
        }
    }

    private static void TryDeleteFileIfExists(string path)
    {
        // delete file if it still exists
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error deleting file at {0}", path);
            }
        }
    }
}