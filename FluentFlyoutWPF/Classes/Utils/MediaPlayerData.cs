// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace FluentFlyout.Classes.Utils;

public static class MediaPlayerData
{
    private class CachedMediaPlayerInfo
    {
        public string Title { get; set; }
        public ImageSource? Icon { get; set; }
    }
    // cache for media player info to avoid redundant process lookups
    private static readonly Dictionary<string, CachedMediaPlayerInfo> mediaPlayerCache = new();

    private static Process[] cachedProcesses = null;
    private static DateTime lastCacheTime = DateTime.MinValue;
    private const int CACHE_DURATION_SECONDS = 5;

    public static (string, ImageSource) getMediaPlayerData(string mediaPlayerId)
    {
        if (mediaPlayerCache.TryGetValue(mediaPlayerId, out var cachedInfo))
        {
            return (cachedInfo.Title, cachedInfo.Icon);
        }

        string mediaTitle = mediaPlayerId;
        ImageSource? mediaIcon = null;
        
        // get sanitized media title name
        string[] mediaSessionIdVariants = mediaPlayerId.Split('.');

        // remove common non-informative substrings
        var variants = mediaSessionIdVariants.Select(variant =>
            variant.Replace("com", "", StringComparison.OrdinalIgnoreCase)
                   .Replace("github", "", StringComparison.OrdinalIgnoreCase)
                   .Replace("exe", "", StringComparison.OrdinalIgnoreCase)
                   .Trim()
        ).Where(variant => !string.IsNullOrWhiteSpace(variant)).ToList();

        // add original id to the end of the array to ensure at least one variant
        variants.Add(mediaPlayerId);

        Process[] processes;

        // use cache to avoid frequent process enumeration
        if (cachedProcesses == null || (DateTime.Now - lastCacheTime).TotalSeconds > CACHE_DURATION_SECONDS)
        {
            cachedProcesses = Process.GetProcesses();
            lastCacheTime = DateTime.Now;
        }

        processes = cachedProcesses;

        var processData = processes.Select(p =>
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
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(processData.Path))
                {
                    if (icon != null)
                    {
                        mediaIcon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        mediaIcon.Freeze();
                    }
                }
            }
            catch
            {
                mediaIcon = null;
            }
        }

        mediaPlayerCache[mediaPlayerId] = new CachedMediaPlayerInfo
        {
            Title = mediaTitle,
            Icon = mediaIcon
        };

        if (mediaIcon != null)
        {
            // Cached against both for consistency.
            SaveIconToDisk(mediaTitle, mediaIcon);
            SaveIconToDisk(mediaPlayerId, mediaIcon);
        }

        return (mediaTitle, mediaIcon);
    }

    /// <summary>
    /// Saves the specified application icon to disk as a PNG file in the user's application data directory. (Usually )
    /// </summary>
    /// <remarks>If the icon is not a BitmapSource, the method does not perform any operation. The icon is
    /// saved in a subdirectory named 'FluentFlyout\AppIcons' under the user's application data folder. If a file with the same name exists,
    /// it will be overriden.</remarks>
    /// <param name="appName">The name of the application. Used to generate the file name for the saved icon. Cannot contain invalid file name
    /// characters.</param>
    /// <param name="icon">The icon to save. Must be an ImageSource that is a BitmapSource, otherwise, the method does nothing.</param>
    public static void SaveIconToDisk(string appName, ImageSource icon)
    {
        if (icon is not BitmapSource bitmapSource) return;

        try
        {
            var cacheDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluentFlyout", "AppIcons");
            System.IO.Directory.CreateDirectory(cacheDir);

            var safeAppName = string.Join("_", appName.Split(System.IO.Path.GetInvalidFileNameChars()));
            var filePath = System.IO.Path.Combine(cacheDir, safeAppName + ".png");

            using var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Create);

            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            encoder.Save(fileStream);
        }
        catch { }
    }

    public static ImageSource? GetIconFromDisk(string appName)
    {
        try
        {
            var cacheDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluentFlyout", "AppIcons");
            var safeAppName = string.Join("_", appName.Split(System.IO.Path.GetInvalidFileNameChars()));
            var filePath = System.IO.Path.Combine(cacheDir, safeAppName + ".png");

            if (System.IO.File.Exists(filePath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath);
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
        }
        catch { }

        return null;
    }
}