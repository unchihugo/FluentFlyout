// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static FluentFlyout.Classes.NativeMethods;
namespace FluentFlyout.Classes.Utils;

public static class MediaPlayerData
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private class CachedMediaPlayerInfo
    {
        public required string Title { get; set; }
        public ImageSource? Icon { get; set; }
        public int ProcessId { get; set; }
    }
    // cache for media player info to avoid redundant process lookups
    private static readonly ConcurrentDictionary<string, CachedMediaPlayerInfo> mediaPlayerCache = [];

    // id variants of media players where the key is the mediaPlayerId and the value is the mediaPlayerCache key
    private static readonly ConcurrentDictionary<string, string> mediaPlayerIdVariants = [];

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
        if (variants.Any(v => v.Contains("MicrosoftEdge", StringComparison.OrdinalIgnoreCase)
            || v.Equals("MSEdge", StringComparison.OrdinalIgnoreCase)
            || v.EndsWith("!MSEdge", StringComparison.OrdinalIgnoreCase))) variants.Add("msedge");

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
                    // pre-filter processes without a main window handle unless they are an exact match to the media player id
                    bool isExactMatch = variants.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase);
                    if (!isExactMatch && p.MainWindowHandle == IntPtr.Zero)
                    {
                        return null;
                    }

                    var mainModule = p.MainModule;
                    if (mainModule == null) return null;

                    string path = mainModule.FileName;

                    if (isExactMatch || variants.Any(v => path.Contains(v, StringComparison.OrdinalIgnoreCase)))
                    {
                        // prioritize the FileDescription for a user-friendly name
                        // fall back to MainWindowTitle if the description is empty
                        string title = !string.IsNullOrWhiteSpace(mainModule.FileVersionInfo.FileDescription)
                                        ? mainModule.FileVersionInfo.FileDescription
                                        : !string.IsNullOrWhiteSpace(p.MainWindowTitle)
                                            ? p.MainWindowTitle
                                            : p.ProcessName;

                        return new { Title = title, Path = path, ProcessId = p.Id, IsExactMatch = isExactMatch };
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // silently ignore the exception for inaccessible processes
                }
                catch (InvalidOperationException)
                {
                    // process exited while being inspected
                }
                return null;
            })
            .OrderByDescending(data => data != null && data.IsExactMatch)
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

    public static int? GetAndCacheProcessId(string mediaPlayerId)
    {
        GetAndCacheMediaPlayerData(mediaPlayerId);

        if (!mediaPlayerCache.TryGetValue(mediaPlayerId, out var cachedInfo)
            && (!mediaPlayerIdVariants.TryGetValue(mediaPlayerId, out var variantKey)
            || !mediaPlayerCache.TryGetValue(variantKey, out cachedInfo))) return null;

        return cachedInfo.ProcessId > 0 ? cachedInfo.ProcessId : null;
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
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to activate media player");
        }

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
                    if (IsIconic(handle))
                    {
                        ShowWindow(handle, SW_RESTORE);
                        Thread.Sleep(100);
                    }

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
        new[] { "chrome", "msedge" }
        .Contains(processName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the executable path of a process, including elevated/admin
    /// processes that deny <see cref="Process.MainModule"/> to a non-elevated
    /// caller. <c>PROCESS_QUERY_LIMITED_INFORMATION</c> plus
    /// <c>QueryFullProcessImageName</c> is granted for such processes, and file
    /// metadata/icon reads work on the path without opening the process.
    /// Returns null only when the process is gone or fully inaccessible.
    /// </summary>
    public static string? TryGetProcessPath(int processId)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
            if (handle != IntPtr.Zero)
            {
                var builder = new System.Text.StringBuilder(1024);
                int size = builder.Capacity;
                if (QueryFullProcessImageName(handle, 0, builder, ref size) && size > 0)
                    return builder.ToString(0, size);
            }
        }
        catch
        {
            // fall through to the MainModule attempt below
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                try { CloseHandle(handle); } catch { }
            }
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
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

            var path = TryGetProcessPath(processId);
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