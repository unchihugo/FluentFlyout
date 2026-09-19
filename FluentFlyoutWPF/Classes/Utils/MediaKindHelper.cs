// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.IO;
using System.Text;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyout.Classes.Utils;

/// <summary>
/// The kind of media something is playing.
/// </summary>
public enum MediaKind
{
    /// <summary>Nothing could be determined.</summary>
    Unknown,

    /// <summary>Only sound is playing.</summary>
    Audio,

    /// <summary>Sound and a picture are playing.</summary>
    Video
}

/// <summary>
/// Tells audio and video apart for whatever media session is playing.
/// <para>
/// The media session offers a playback type, but players report it unreliably: PotPlayer reports "music" for
/// video files as well, and browsers do the same for some pages. It is therefore only the last hint. The
/// reliable signals are the file extension in the title of the window a player shows its file in, and, for the
/// known local players, the shape of their video surface (a song leaves a flat, wide strip while a video leaves
/// a roughly screen shaped area at the top left of the player window).
/// </para>
/// </summary>
public static class MediaKindHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // Players report their playback type themselves; PotPlayer reports Music for video files, so the file
    // extension in the title is what really tells the two apart.
    private static readonly string[] VideoFileExtensions =
    [
        ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".flv", ".f4v", ".mpg", ".mpeg", ".m1v",
        ".m2v", ".ts", ".m2ts", ".mts", ".vob", ".rm", ".rmvb", ".asf", ".divx", ".3gp", ".ogv", ".ogm",
        ".mp4v", ".mpe", ".mpv", ".mxf", ".wtv", ".dvr-ms"
    ];

    private static readonly string[] AudioFileExtensions =
    [
        ".mp3", ".mp2", ".mpga", ".m4a", ".m4b", ".m4p", ".aac", ".flac", ".wav", ".wave", ".wma", ".ogg",
        ".oga", ".opus", ".ape", ".alac", ".aiff", ".aif", ".mka", ".dsf", ".dff", ".tak", ".wv", ".amr"
    ];

    // process names of the players whose video surface can be recognised
    private static readonly string[] LocalVideoPlayerProcessNames =
    [
        "PotPlayerMini64", "PotPlayerMini", "PotPlayer64", "PotPlayer",
        "vlc", "mpv", "mpc-hc64", "mpc-hc", "mpc-be64", "mpc-be",
        "KMPlayer", "KMPlayer64", "GOM", "gom64", "mpcvr"
    ];

    // the shape a video surface has: a decent part of the client area, at the top left corner, not a tall panel
    // and not a flat strip (PotPlayer leaves a visible 800x80 strip in the top left corner while it plays a
    // song, which is ten times as wide as it is tall, while a 16:9 picture is 1.78 times as wide)
    private const int MinVideoSurfaceAreaPercent = 25;
    private const int LeftEdgeTolerance = 40;
    private const int TopEdgeTolerance = 120;
    private const double MinVideoSurfaceAspect = 1.0;
    private const double MaxVideoSurfaceAspect = 4.0;

    /// <summary>
    /// Whether the title looks like it belongs to a video file.
    /// </summary>
    public static bool TitleLooksLikeVideo(string? title) => LastExtensionMatch(title, VideoFileExtensions) >= 0;

    /// <summary>
    /// Whether the title looks like it belongs to a song.
    /// </summary>
    public static bool TitleLooksLikeAudio(string? title) => LastExtensionMatch(title, AudioFileExtensions) >= 0;

    /// <summary>
    /// Gets the title of a window (players put the file they play there).
    /// </summary>
    public static string GetWindowTitle(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
            return string.Empty;

        var titleBuffer = new StringBuilder(512);
        GetWindowText(window, titleBuffer, titleBuffer.Capacity);
        return titleBuffer.ToString();
    }

    /// <summary>
    /// Whether the given process belongs to one of the local video players whose video surface can be
    /// recognised. When the process id is no longer valid, the given name is used instead.
    /// </summary>
    /// <param name="processId">The process to look at.</param>
    /// <param name="processName">
    /// The name to fall back to when the process id is gone, with or without a file extension. Media sessions
    /// are named after the process that owns them, so this can be the session id.
    /// </param>
    public static bool IsLocalVideoPlayer(int processId, string? processName = null)
    {
        string? name = GetProcessName(processId) ?? NormalizeProcessName(processName);

        if (string.IsNullOrEmpty(name))
            return false;

        return Array.Exists(LocalVideoPlayerProcessNames,
            candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the window a process shows its media in, or <see cref="IntPtr.Zero"/>. When the process id is no
    /// longer valid, the given name is used instead.
    /// </summary>
    /// <param name="processId">The process to look at.</param>
    /// <param name="processName">
    /// The name to fall back to when the process id is gone, with or without a file extension.
    /// </param>
    public static IntPtr GetMainWindow(int processId, string? processName = null)
    {
        // the cached process id of a session can be stale (a player was restarted), so the name is the fallback
        string? name = GetProcessName(processId) ?? NormalizeProcessName(processName);

        if (string.IsNullOrEmpty(name))
            return IntPtr.Zero;

        foreach (var candidate in Process.GetProcessesByName(name))
        {
            using (candidate)
            {
                if (candidate.MainWindowHandle != IntPtr.Zero)
                    return candidate.MainWindowHandle;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Gets the name of a living process, or null when it is gone.
    /// </summary>
    private static string? GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to look up the process {0}", processId);
            return null;
        }
    }

    /// <summary>
    /// Gets a process name without its file extension ("PotPlayerMini64.exe" becomes "PotPlayerMini64").
    /// </summary>
    private static string? NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        string name = Path.GetFileNameWithoutExtension(processName.Trim());
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Whether a player window has a child window that looks like a video surface. Only meaningful for the
    /// local players listed in <see cref="LocalVideoPlayerProcessNames"/>; browsers and other applications do
    /// not render into child windows.
    /// </summary>
    public static bool HasVideoSurface(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window) || !GetClientRect(window, out RECT clientArea))
            return false;

        int clientWidth = clientArea.Right - clientArea.Left;
        int clientHeight = clientArea.Bottom - clientArea.Top;

        if (clientWidth <= 0 || clientHeight <= 0)
            return false;

        var clientOrigin = new POINT { X = clientArea.Left, Y = clientArea.Top };
        if (!ClientToScreen(window, ref clientOrigin))
            return false;

        bool found = false;

        EnumChildWindows(window, (child, _) =>
        {
            if (found || !IsWindowVisible(child) || !GetWindowRect(child, out RECT childArea))
                return true;

            int width = childArea.Right - childArea.Left;
            int height = childArea.Bottom - childArea.Top;

            if (width <= 0 || height <= 0)
                return true;

            // a video surface covers a good part of the player window, starts at its top left corner and is not
            // a tall panel (a playlist next to the video is taller than wide and starts further to the right)
            if ((long)width * height * 100 < (long)clientWidth * clientHeight * MinVideoSurfaceAreaPercent)
                return true;

            if (childArea.Left - clientOrigin.X > LeftEdgeTolerance || childArea.Top - clientOrigin.Y > TopEdgeTolerance)
                return true;

            if (width < height * MinVideoSurfaceAspect || width > height * MaxVideoSurfaceAspect)
                return true;

            found = true;
            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// Determines the kind of media a process is showing.
    /// </summary>
    /// <param name="processId">The process that owns the media session.</param>
    /// <param name="mediaTitle">The title the media session reports, if any.</param>
    /// <param name="reportedPlaybackType">
    /// The playback type the media session reports ("Video", "Music", ...), if any. Used as the last hint only,
    /// because players report it unreliably.
    /// </param>
    /// <param name="processName">
    /// The name to fall back to when the process id is gone, with or without a file extension (media sessions
    /// are named after the process that owns them, so this can be the session id).
    /// </param>
    /// <returns>The determined kind, <see cref="MediaKind.Unknown"/> when nothing points anywhere.</returns>
    public static MediaKind GetMediaKind(int processId, string? mediaTitle = null,
        string? reportedPlaybackType = null, string? processName = null)
    {
        IntPtr window = GetMainWindow(processId, processName);
        string windowTitle = GetWindowTitle(window);

        // 1) the file name a player shows in its window title is the most reliable signal
        MediaKind fromWindowTitle = KindFromTitle(windowTitle);
        if (fromWindowTitle != MediaKind.Unknown)
            return fromWindowTitle;

        // 2) some players report the file name as the media title instead
        MediaKind fromMediaTitle = KindFromTitle(mediaTitle);
        if (fromMediaTitle != MediaKind.Unknown)
            return fromMediaTitle;

        // 3) for a known local player the shape of its surface is an observation rather than a claim, so it
        //    wins over what the player reports about itself: a song leaves a flat strip, a video leaves a
        //    screen shaped area (the surface appears a moment after the media started, so a video that was
        //    just opened may still be reported as audio here)
        if (window != IntPtr.Zero && IsLocalVideoPlayer(processId, processName))
            return HasVideoSurface(window) ? MediaKind.Video : MediaKind.Audio;

        // 4) what the application claims (unreliable for players, see the class remarks, but it is what is
        //    left for applications that do not render into child windows, such as browsers)
        if (string.Equals(reportedPlaybackType, "Video", StringComparison.OrdinalIgnoreCase))
            return MediaKind.Video;

        if (string.Equals(reportedPlaybackType, "Music", StringComparison.OrdinalIgnoreCase))
            return MediaKind.Audio;

        return MediaKind.Unknown;
    }

    /// <summary>
    /// Determines the kind of media a process is showing, giving the player the moment it needs to lay its
    /// surface out after a track change. A player that switches between a song and a video still shows the
    /// previous layout for a moment, so a single reading right after the change can be wrong; this waits and
    /// confirms the reading before answering.
    /// </summary>
    /// <param name="processId">The process that owns the media session.</param>
    /// <param name="mediaTitle">The title the media session reports, if any.</param>
    /// <param name="reportedPlaybackType">The playback type the media session reports, if any.</param>
    /// <param name="processName">The name to fall back to when the process id is gone, if any.</param>
    /// <param name="settleMilliseconds">How long to wait for the player to settle, at least 0.</param>
    /// <returns>The determined kind, <see cref="MediaKind.Unknown"/> when nothing points anywhere.</returns>
    public static async Task<MediaKind> GetMediaKindAsync(int processId, string? mediaTitle = null,
        string? reportedPlaybackType = null, string? processName = null, int settleMilliseconds = 700)
    {
        settleMilliseconds = Math.Max(0, settleMilliseconds);

        if (settleMilliseconds > 0)
            await Task.Delay(settleMilliseconds).ConfigureAwait(true);

        MediaKind settled = GetMediaKind(processId, mediaTitle, reportedPlaybackType, processName);

        // confirm it with a second reading so that a layout that was still changing does not decide alone
        if (settleMilliseconds > 0)
        {
            await Task.Delay(Math.Max(150, settleMilliseconds / 2)).ConfigureAwait(true);
            settled = GetMediaKind(processId, mediaTitle, reportedPlaybackType, processName);
        }

        return settled;
    }

    /// <summary>
    /// The kind a title points at, using the last matching extension so that a file name that happens to
    /// contain another extension elsewhere still wins with its real one.
    /// </summary>
    private static MediaKind KindFromTitle(string? title)
    {
        int video = LastExtensionMatch(title, VideoFileExtensions);
        int audio = LastExtensionMatch(title, AudioFileExtensions);

        if (video < 0 && audio < 0)
            return MediaKind.Unknown;

        return video > audio ? MediaKind.Video : MediaKind.Audio;
    }

    /// <summary>
    /// The position of the last occurrence of any of the extensions in the title, or -1.
    /// </summary>
    private static int LastExtensionMatch(string? title, string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(title))
            return -1;

        int best = -1;

        foreach (string extension in extensions)
        {
            int index = title.LastIndexOf(extension, StringComparison.OrdinalIgnoreCase);

            if (index > best)
                best = index;
        }

        return best;
    }
}
