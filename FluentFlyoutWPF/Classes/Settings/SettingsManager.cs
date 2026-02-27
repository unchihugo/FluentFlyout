// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes.Utils;
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

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentFlyout",
        "settings.xml"
    );
    string logFilePath = Path.Combine(FileSystemHelper.GetLogsPath(), "FluentFlyout", "log.txt");
    private static UserSettings _current;

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

    /// <summary>
    /// Checks whether the app has updated, and restores the settings from the previous version if necessary. Only updates in release mode.
    /// </summary>
    //public static void CheckAndUpdateSettings()
    //{
    //    try // gets the version of the app, works only in release mode
    //    {
    //        var version = Package.Current.Id.Version;
    //        string versionString = $"{version.Major}.{version.Minor}.{version.Build}";
    //        // if local settings file's version number is different from the current version, update the settings
    //        if (!File.Exists(SettingsFilePath)) return;
    //        if (Settings.Default.LastKnownVersion != versionString)
    //        {
    //            RestoreSettings();
    //        }
    //    }
    //    catch
    //    {
    //        // don't update settings if version is not available
    //        return;
    //    }
    //}

    /// <summary>
    /// Restores the settings `SettingsManager.Current` from the settings file.
    /// </summary>
    /// <returns>The restored settings.</returns>
    public UserSettings RestoreSettings()
    {
        //File.AppendAllText(logFilePath, $"[{DateTime.Now}] {SettingsFilePath}\n");
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                using (StreamReader reader = new StreamReader(SettingsFilePath))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(UserSettings));
                    _current = (UserSettings)xmlSerializer.Deserialize(reader);
                    _current.CompleteInitialization();

                    Logger.Info("Settings successfully restored");
                    return _current;
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error(ex, "No permission to write in settings file");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error restoring settings");
        }

        // if the settings file not found or cannot be read
        Logger.Warn("Settings file not found or cannot be read, loading default settings");
        _current = new UserSettings();
        _current.CompleteInitialization();
        return _current;
    }

    /// <summary>
    /// Saves the app settings to the settings file.
    /// </summary>
    public static void SaveSettings()
    {
        try
        {
            string directory = Path.GetDirectoryName(SettingsFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (StreamWriter writer = new StreamWriter(SettingsFilePath))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(UserSettings));
                xmlSerializer.Serialize(writer, _current);
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

    /// <summary>
    /// Exports the current settings to a file selected by the user.
    /// </summary>
    /// <returns>True if export was successful, false otherwise.</returns>
    public static bool ExportSettings(string filePath)
    {
        try
        {
            using (StreamWriter writer = new (filePath))
            {
                XmlSerializer xmlSerializer = new (typeof(UserSettings));
                xmlSerializer.Serialize(writer, _current);
            }
            Logger.Info($"Settings successfully exported to {filePath}");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error(ex, "No permission to write to export file");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error exporting settings");
            return false;
        }
    }

    /// <summary>
    /// Imports settings from a file selected by the user.
    /// </summary>
    /// <returns>True if import was successful, false otherwise.</returns>
    public static bool ImportSettings(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Logger.Warn($"Import file not found: {filePath}");
                return false;
            }

            using StreamReader reader = new(filePath);
            XmlSerializer xmlSerializer = new(typeof(UserSettings));

            if (xmlSerializer.Deserialize(reader) is UserSettings importedSettings)
            {
                _current = importedSettings;
                _current.CompleteInitialization();
                SaveSettings();
                Logger.Info("Settings successfully imported");
                return true;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error(ex, "No permission to read import file");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error importing settings");
        }

        return false;
    }
}
