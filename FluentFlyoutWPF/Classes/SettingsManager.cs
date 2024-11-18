using FluentFlyout.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.ApplicationModel;

namespace FluentFlyout.Classes
{
    class SettingsManager
    {
        private static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FluentFlyout",
            "settings.json"
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
                if (!File.Exists(SettingsFilePath) || Settings.Default.LastKnownVersion != versionString) return;
                else
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

                        foreach (System.Configuration.SettingsProperty property in settingsFile.Properties)
                        {
                            Settings.Default[property.Name] = settingsFile[property.Name];
                        }
                        Settings.Default.Save();
                    }
                }
            }
            catch
            {
                // if settings file is corrupted or cannot be read
            }
        }

        /// <summary>
        /// Saves the app settings to the settings file.
        /// </summary>
        public static void SaveToSettingsFile()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));

                XmlSerializer xmlSerializer = new XmlSerializer(typeof(Settings));
                using (FileStream fileStream = new FileStream(SettingsFilePath, FileMode.Create))
                {
                    xmlSerializer.Serialize(fileStream, Settings.Default);
                }
            }
            catch
            {
                // could not save settings to file
            }
        }
    }
}
