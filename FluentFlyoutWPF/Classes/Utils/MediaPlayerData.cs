// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static FluentFlyout.Classes.NativeMethods;
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

    public static bool TryActivateMediaPlayer(string mediaPlayerId, string? mediaTitle = null)
    {
        GetAndCacheMediaPlayerData(mediaPlayerId);
        if (!mediaPlayerCache.TryGetValue(mediaPlayerId, out var cachedInfo)
            && (!mediaPlayerIdVariants.TryGetValue(mediaPlayerId, out var variantKey)
            || !mediaPlayerCache.TryGetValue(variantKey, out cachedInfo))) return false;

        try
        {
            using var process = Process.GetProcessById(cachedInfo.ProcessId);
            IntPtr handle = process.MainWindowHandle;
            if (IsBrowser(process.ProcessName)) return TryActivateBrowserTab(process.ProcessName, mediaTitle);

            if (handle == IntPtr.Zero)
            {
                foreach (var candidate in Process.GetProcessesByName(process.ProcessName))
                {
                    handle = candidate.MainWindowHandle;
                    candidate.Dispose();
                    if (handle != IntPtr.Zero) break;
                }
            }

            if (handle != IntPtr.Zero)
            {
                if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
                return SetForegroundWindow(handle);
            }

            string? path = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path) && !IsBrowser(System.IO.Path.GetFileNameWithoutExtension(path)))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }
        }
        catch { }

        return false;
    }

    private static bool TryActivateBrowserTab(string processName, string? mediaTitle)
    {
        if (!IsBrowser(processName) || string.IsNullOrWhiteSpace(mediaTitle)) return false;

        foreach (var browserProcess in Process.GetProcessesByName(processName))
        {
            try
            {
                var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.ProcessIdProperty, browserProcess.Id),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)));

                foreach (AutomationElement window in windows)
                {
                    IntPtr handle = new(window.Current.NativeWindowHandle);
                    if (handle == IntPtr.Zero) continue;

                    var tabs = window.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));

                    foreach (AutomationElement tab in tabs)
                    {
                        if (!tab.Current.Name.Contains(mediaTitle, StringComparison.OrdinalIgnoreCase)) continue;
                        if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
                            ((SelectionItemPattern)pattern).Select();
                        else if (tab.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                            ((InvokePattern)pattern).Invoke();
                        else continue;

                        if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
                        return SetForegroundWindow(handle);
                    }
                }
            }
            catch { }
            finally
            {
                browserProcess.Dispose();
            }
        }

        return false;
    }

    private static bool IsBrowser(string processName) =>
        new[] { "chrome", "msedge", "firefox", "brave", "brave-browser", "opera", "vivaldi" }
        .Contains(processName, StringComparer.OrdinalIgnoreCase);

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
}