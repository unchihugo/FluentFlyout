using FluentFlyout.Classes.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using System.Windows;
using System.IO;

namespace FluentFlyoutWPF.Classes.Utils
{
    internal class FileSystemHelper
    {
        public static string GetLogsPath()
        {
            return SettingsManager.Current.IsStoreVersion
                ? Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "Roaming", "FluentFlyout")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluentFlyout");
        }
    }
}
