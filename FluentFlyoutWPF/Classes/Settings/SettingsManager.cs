// Copyright © 2024-2026 The FluentFlyout Authors
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

    private static UserSettings _current;

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
            if (_current == null)
            {
                _current = new UserSettings();
            }
            return _current;
        }
        set => _current = value;
    }

    internal static bool IsCurrentSettings(UserSettings settings)
    {
        return ReferenceEquals(_current, settings);
    }

    /// <summary>
    /// Restores the settings `SettingsManager.Current` from the settings file.
    /// </summary>
    /// <returns>The restored settings.</returns>
    public static UserSettings RestoreSettings(string? filePath = null)
    {
        filePath ??= SettingsFilePath;
        string backupPath = filePath + ".bak";

        try
        {
            if (DeserializeSettings(filePath, out var loadedSettings) && loadedSettings != null)
            {
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
        filePath ??= SettingsFilePath;
        string tempPath = filePath + ".tmp";
        string backupPath = filePath + ".bak";

        try
        {
            lock (SettingsFileLock)
            {
                string directory = Path.GetDirectoryName(filePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _current ??= new UserSettings();

                using (var writer = new StreamWriter(tempPath, false))
                {
                    XmlSerializer xmlSerializer = new(typeof(UserSettings));
                    xmlSerializer.Serialize(writer, _current);
                }

                if (File.Exists(filePath))
                {
                    File.Replace(tempPath, filePath, backupPath, true);
                }
                else
                {
                    File.Move(tempPath, filePath, true);
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
        finally
        {
            // delete temp file if it still exists
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error deleting temporary settings file");
                }
            }
        }
    }
}
