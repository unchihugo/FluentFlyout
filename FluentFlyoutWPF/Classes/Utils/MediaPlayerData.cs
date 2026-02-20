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
        public int ProcessId { get; set; }
    }
    // cache for media player info to avoid redundant process lookups
    private static readonly Dictionary<string, CachedMediaPlayerInfo> mediaPlayerCache = new();

    private static Process[] cachedProcesses = null;
    private static DateTime lastCacheTime = DateTime.MinValue;
    private const int CACHE_DURATION_SECONDS = 5;

    public static (string, ImageSource?) getMediaPlayerData(string mediaPlayerId)
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

                        return new { Title = title, Path = path, ProcessId = p.Id };
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // silently ignore the exception for inaccessible processes
                }
                return null;
            })
            .FirstOrDefault(data => data != null); // use first result

        if (processData == null) return (mediaTitle, mediaIcon);

        mediaTitle = !string.IsNullOrWhiteSpace(processData.Title) ? processData.Title : mediaPlayerId;

        // check cache again because we have the sanitized title
        if (mediaPlayerCache.TryGetValue(mediaTitle, out cachedInfo))
        {
            return (cachedInfo.Title, cachedInfo.Icon);
        }

        mediaIcon = GetIconFromPath(processData.Path);

        mediaPlayerCache[mediaPlayerId] = new CachedMediaPlayerInfo
        {
            Title = mediaTitle,
            Icon = mediaIcon,
            ProcessId = processData.ProcessId
        };

        return (mediaTitle, mediaIcon);
    }

    /// <summary>
    /// Extracts the associated icon for a given process ID. Returns null if the process is inaccessible.
    /// </summary>
    public static ImageSource? GetProcessIcon(int processId, string title)
    {
        try
        {
            if (title == "System sounds") return null;

            // search in cache
            foreach (var item in mediaPlayerCache.Values)
            {
                if (item.ProcessId == processId)
                {
                    return item.Icon;
                }
            }

            var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            if (path == null) return null;

            // store in cache for future lookups
            var icon = GetIconFromPath(path);
            if (icon != null)
            {
                mediaPlayerCache[title] = new CachedMediaPlayerInfo
                {
                    Title = title,
                    Icon = icon,
                    ProcessId = processId
                };
            }

            return icon;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? GetIconFromPath(string exePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();

            return source;
        }
        catch
        {
            return null;
        }
    }
}