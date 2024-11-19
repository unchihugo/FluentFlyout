using FluentFlyout.Properties;
using System.Diagnostics;
using System.IO;
using System.Xml.Serialization;
using Windows.ApplicationModel;

namespace FluentFlyout.Classes;

/// <summary>
/// Manages the application settings and saves them to a file in \AppData\FluentFlyout.
/// </summary>
internal static class SettingsManager
{
    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentFlyout",
        "settings.xml"
    );

    /// <summary>
    /// Checks whether the app has updated, and restores the settings from the previous version if necessary. Only updates in release mode.
    /// </summary>
    public static void CheckAndUpdateSettings()
    {
        try // gets the version of the app, works only in release mode
        {
            var version = Package.Current.Id.Version;
            string versionString = $"{version.Major}.{version.Minor}.{version.Build}";
            // if local settings file's version number is different from the current version, update the settings
            if (!File.Exists(SettingsFilePath)) return;
            if (Settings.Default.LastKnownVersion != versionString)
            {
                RestoreSettings();
            }
        }
        catch
        {
            // don't update settings if version is not available
            return;
        }
    }

    /// <summary>
    /// Restores the app settings from the settings file.
    /// </summary>
    private static void RestoreSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(Settings));
                using (FileStream fileStream = new FileStream(SettingsFilePath, FileMode.Open))
                {
                    Settings settingsFile = (Settings)xmlSerializer.Deserialize(fileStream);

                    var properties = typeof(Settings).GetProperties();
                    foreach (var property in properties)
                    {
                        try
                        {
                            var value = property.GetValue(settingsFile);
                            property.SetValue(Settings.Default, value);
                        }
                        catch (Exception ex)
                        {
                            string errorLog = $"Error restoring setting: {property.Name} - {ex.Message}";
                            EventLog.WriteEntry("FluentFlyout", errorLog, EventLogEntryType.Warning);
                        }
                    }
                    Settings.Default.Save();
                    EventLog.WriteEntry("FluentFlyout", "Settings restored", EventLogEntryType.Information);
                }
            }
        }
        catch (Exception ex)
        {
            string errorLog = "Settings file is corrupted or cannot be read";
            EventLog.WriteEntry("FluentFlyout", errorLog, EventLogEntryType.Error);
        }
    }

    /// <summary>
    /// Saves the app settings to the settings file.
    /// </summary>
    public static void SaveToSettingsFile()
    {
        try
        {
            string directory = Path.GetDirectoryName(SettingsFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            XmlSerializer xmlSerializer = new XmlSerializer(typeof(Settings));
            using (FileStream fileStream = new FileStream(SettingsFilePath, FileMode.Create))
            {
                xmlSerializer.Serialize(fileStream, Settings.Default);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // if the app doesn't have permission to write to the settings file
        }
        catch (Exception ex)
        {
            // if the settings file cannot be saved
        }
    }
}
