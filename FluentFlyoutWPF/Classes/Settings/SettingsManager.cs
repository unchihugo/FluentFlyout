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
    private static XmlSerializer? _exportSerializer;

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
    /// Saves the app settings to the settings file.
    /// </summary>
    public static void SaveSettings(string? filePath = null)
    {
        bool isExport = filePath != null;
        filePath ??= SettingsFilePath;
        string tempPath = filePath + ".tmp";
        string backupPath = filePath + ".bak";

        try
        {
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
                    XmlSerializer xmlSerializer;
                    if (isExport)
                    {
                        xmlSerializer = GetExportSerializer();
                    }
                    else
                    {
                        xmlSerializer = new XmlSerializer(typeof(UserSettings));
                    }
                    xmlSerializer.Serialize(writer, _current);
                }

                if (File.Exists(filePath))
                    _ = Task.Run(async () =>
                    {
                        // Run asynchronously to avoid blocking the UI thread
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
        }
        catch (UnauthorizedAccessException ex)
        {
            // if the app doesn't have permission to write to the settings file
            Logger.Error(ex, "No permission to write in settings file");
        }
        catch (Exception ex)
        {
            // if the settings file cannot be saved
            Logger.Error(ex, "Error saving settings");
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
                File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
                break;
            }
            catch (IOException ex) when (attempts < maxAttempts)
            {
                // if the file is locked, wait and retry
                Logger.Warn(ex, "Settings file is locked, retrying...");
                Thread.Sleep(75);
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
        TryDeleteFileIfExists(backupPath);
        File.Copy(filePath, backupPath);
        TryDeleteFileIfExists(filePath);
        File.Move(tempPath, filePath);
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