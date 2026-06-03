// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace FluentFlyout.Classes.Utils;

public static class MediaPlayerData
{
    private class CachedMediaPlayerInfo
    {
        public required string Title { get; set; }
        public ImageSource? Icon { get; set; }
        public int ProcessId { get; set; }
    }
    // cache for media player info to avoid redundant process lookups
    private static readonly Dictionary<string, CachedMediaPlayerInfo> mediaPlayerCache = [];

    // id variants of media players where the key is the mediaPlayerId and the value is the mediaPlayerCache key
    private static readonly Dictionary<string, string> mediaPlayerIdVariants = [];

    private static Process[]? cachedProcesses = null;
    private static DateTime lastCacheTime = DateTime.MinValue;
    private const int CACHE_DURATION_SECONDS = 5;

    public static (string, ImageSource?) GetAndCacheMediaPlayerData(string mediaPlayerId)
    {
        if (mediaPlayerCache.TryGetValue(mediaPlayerId, out var cachedInfo)
            || mediaPlayerIdVariants.TryGetValue(mediaPlayerId, out var variantKey)
            && mediaPlayerCache.TryGetValue(variantKey, out cachedInfo))
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

        // Find the first matching process using a single pass and light-weight checks before touching mainModule
        string? foundTitle = null;
        string? foundPath = null;
        int foundPid = 0;

        foreach (var p in processes)
        {
            try
            {
                // skip processes without a visible window early
                if (p.MainWindowHandle == IntPtr.Zero) continue;

                string procName = p.ProcessName ?? string.Empty;
                string windowTitle = p.MainWindowTitle ?? string.Empty;

                // quick check against process name or window title to avoid expensive module access for most processes
                if (!variants.Any(v => procName.Contains(v, StringComparison.OrdinalIgnoreCase) || windowTitle.Contains(v, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var mainModule = p.MainModule;
                if (mainModule == null) continue;

                string path = mainModule.FileName;

                // prioritize the FileDescription for a user-friendly name, fall back to MainWindowTitle
                string? title = !string.IsNullOrWhiteSpace(mainModule.FileVersionInfo.FileDescription)
                                ? mainModule.FileVersionInfo.FileDescription
                                : windowTitle;

                foundTitle = title;
                foundPath = path;
                foundPid = p.Id;

                break; // found the best match, stop scanning further
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // ignore inaccessible processes
                continue;
            }
            catch (InvalidOperationException)
            {
                // process may have exited while checking, ignore and continue
                continue;
            }
            catch
            {
                continue;
            }
        }

        if (foundTitle != null && foundPath != null)
        {
            mediaTitle = !string.IsNullOrWhiteSpace(foundTitle) ? foundTitle : mediaPlayerId;

            // map the original id to the sanitized title for future lookups
            mediaPlayerIdVariants[mediaPlayerId] = mediaTitle;

            try
            {
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(foundPath))
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

        mediaPlayerCache[mediaTitle] = new CachedMediaPlayerInfo
        {
            Title = mediaTitle,
            Icon = mediaIcon,
            ProcessId = foundPid
        };

        return (mediaTitle, mediaIcon);
    }

    /// <summary>
    /// Attempts to load an icon for a given process ID by looking up its main module.
    /// </summary>
    public static ImageSource? GetAndCacheProcessIcon(int processId, string title)
    {
        try
        {
            if (title == "System sounds") return null;

            // search in cache
            foreach (var item in mediaPlayerCache.Values)
            {
                if (item.ProcessId == processId && item.Icon != null)
                {
                    return item.Icon;
                }
            }

            // Attempt to load via process MainModule
            var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            if (path == null) return null;

            var icon = LoadIconFromPath(path);
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

    private static ImageSource? LoadIconFromPath(string exePath)
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
