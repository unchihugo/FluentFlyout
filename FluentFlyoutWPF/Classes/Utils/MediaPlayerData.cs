using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentFlyout.Classes.Utils;

public static class MediaPlayerData
{
    public static (string, ImageSource) getMediaPlayerData(string mediaPlayerId)
    {
        string mediaTitle = mediaPlayerId;
        ImageSource? mediaIcon = null;

        // get sanitized media title name
        string[] mediaSessionIdVariants = mediaPlayerId.Split('.');

        // remove common non-informative substrings
        var variants = mediaSessionIdVariants.Select(variant =>
            variant.Replace("com", "", StringComparison.OrdinalIgnoreCase)
                   .Replace("github", "", StringComparison.OrdinalIgnoreCase)
                   .Trim()
        ).Where(variant => !string.IsNullOrWhiteSpace(variant)).ToList();

        // add original id to the end of the array to ensure at least one variant
        variants.Add(mediaPlayerId);

        var processData = Process.GetProcesses()
        .Select(p =>
        {
            try
            {
                // pre-filter processes without a main window handle
                if (p.MainWindowHandle == IntPtr.Zero)
                {
                    return null;
                }

                var mainModule = p.MainModule;
                if (mainModule == null) return null;

                string path = mainModule.FileName;

                if (variants.Any(v => path.Contains(v, StringComparison.OrdinalIgnoreCase)))
                {
                    // prioritize the FileDescription for a user-friendly name
                    // fall back to MainWindowTitle if the description is empty
                    string title = !string.IsNullOrWhiteSpace(mainModule.FileVersionInfo.FileDescription)
                                    ? mainModule.FileVersionInfo.FileDescription
                                    : p.MainWindowTitle;

                    return new { Title = title, Path = path };
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // silently ignore the exception for inaccessible processes
            }
            return null;
        })
        .FirstOrDefault(data => data != null); // use first result

        if (processData != null)
        {
            mediaTitle = !string.IsNullOrWhiteSpace(processData.Title) ? processData.Title : mediaPlayerId;

            try
            {
                mediaIcon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    System.Drawing.Icon.ExtractAssociatedIcon(processData.Path).Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            catch
            {
                mediaIcon = null;
            }
        }

        return (mediaTitle, mediaIcon);
    }
}