using FluentFlyout.Classes.Settings;
using System.IO;
using Windows.Storage;

namespace FluentFlyoutWPF.Classes.Utils
{
    internal class FileSystemHelper
    {
        public static string GetLogsPath()
        {
            if (SettingsManager.Current.IsStoreVersion)
            {
                return Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "Roaming", "FluentFlyout");
            }
            else
            {
                string path;

                // non-store versions work incredibly weirdly (i haven't figured it out), so we're searching multiple possible locations
                // first, check %appData%\FluentFlyout
                try
                {
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "FluentFlyout");
                    if (Directory.Exists(path))
                        return path;

                }
                catch { }

                // if that doesn't exist, check same path as store version
                try
                {
                    path = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path,
                        "Roaming",
                        "FluentFlyout");
                    if (Directory.Exists(path))
                        return path;
                }
                catch { }

                // if neither of those exist, return hardcoded path
                // %localAppData%\Packages\unchihugo.FluentFlyout_69b7b6qge1ahj\LocalCache\Roaming\FluentFlyout
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages",
                    "unchihugo.FluentFlyout_69b7b6qge1ahj",
                    "LocalCache",
                    "Roaming",
                    "FluentFlyout"
                );
            }
        }
    }
}
