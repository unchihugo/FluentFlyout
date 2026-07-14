// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.Runtime.InteropServices;
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

        if (processData == null)
        {
            var (shellTitle, shellIcon) = ResolveViaAppsFolder(mediaPlayerId);
            if (shellTitle == null) return (mediaTitle, mediaIcon);

            mediaPlayerCache[mediaPlayerId] = new CachedMediaPlayerInfo
            {
                Title = shellTitle,
                Icon = shellIcon,
                ProcessId = -1 // no process match, -1 avoids colliding with real PIDs
            };

            return (shellTitle, shellIcon);
        }

        mediaTitle = !string.IsNullOrWhiteSpace(processData.Title) ? processData.Title : mediaPlayerId;

        // check cache again because we have the sanitized title
        if (mediaPlayerCache.TryGetValue(mediaTitle, out cachedInfo))
        {
            // map the original id to the sanitized title for future lookups
            mediaPlayerIdVariants[mediaPlayerId] = mediaTitle;
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
    public static ImageSource? GetAndCacheProcessIcon(int processId, string title)
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

    /// <summary>
    /// Resolves an AppUserModelId through the shell Apps folder ("shell:AppsFolder"),
    /// the same way the native media flyout does. Handles apps whose session id
    /// doesn't match their process path, e.g. Firefox-based browsers (#949).
    /// </summary>
    private static (string? Title, ImageSource? Icon) ResolveViaAppsFolder(string appUserModelId)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return (null, null);

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic? item = shell.NameSpace("shell:AppsFolder")?.ParseName(appUserModelId);
            if (item == null) return (null, null);

            string name = item.Name;
            if (string.IsNullOrWhiteSpace(name)) return (null, null);

            // desktop apps expose their start menu shortcut target, so the icon can
            // be extracted the same way as everywhere else; packaged apps don't
            // have one and keep a null icon
            var targetPath = item.ExtendedProperty("System.Link.TargetParsingPath") as string;
            ImageSource? icon = targetPath != null ? GetIconFromPath(targetPath) : null;

            return (name, icon);
        }
        catch
        {
            // id is not registered in the apps folder, nothing we can do
            return (null, null);
        }
    }
}