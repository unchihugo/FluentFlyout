// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace FluentFlyoutWPF.Classes;

internal sealed class MusicBeeFallbackSession
{
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public GlobalSystemMediaTransportControlsSessionPlaybackStatus PlaybackStatus { get; init; }
    public IRandomAccessStreamReference? Thumbnail { get; init; }
}

internal static class MusicBeeFallbackProvider
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private const string MusicBeeProcessName = "MusicBee";

    public static bool TryGetCurrentSession(out MusicBeeFallbackSession? session)
    {
        session = null;
        Process? process = GetMusicBeeProcess();
        if (process == null)
            return false;

        string? windowTitle = null;
        try
        {
            windowTitle = process.MainWindowTitle?.Trim();
        }
        catch
        {
            // Process could exit while reading title.
            return false;
        }

        if (!TryParseTrack(windowTitle, out string title, out string artist))
            return false;

        var playbackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        IRandomAccessStreamReference? thumbnail = null;
        TryGetMusicBeeSmtcData(ref title, ref artist, ref playbackStatus, ref thumbnail);

        session = new MusicBeeFallbackSession
        {
            Title = title,
            Artist = artist,
            PlaybackStatus = playbackStatus,
            Thumbnail = thumbnail
        };

        return true;
    }

    private static Process? GetMusicBeeProcess()
    {
        try
        {
            return Process.GetProcessesByName(MusicBeeProcessName)
                .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to enumerate MusicBee process");
            return null;
        }
    }

    private static bool TryParseTrack(string? windowTitle, out string title, out string artist)
    {
        title = string.Empty;
        artist = string.Empty;

        if (string.IsNullOrWhiteSpace(windowTitle))
            return false;

        // Typical MusicBee title pattern: "Artist - Track - MusicBee".
        string normalized = windowTitle.Trim();
        if (normalized.Equals("MusicBee", StringComparison.OrdinalIgnoreCase))
            return false;

        string[] parts = normalized
            .Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return false;

        if (parts[^1].Equals("MusicBee", StringComparison.OrdinalIgnoreCase))
            parts = parts.Take(parts.Length - 1).ToArray();

        if (parts.Length == 0)
            return false;

        if (parts.Length == 1)
        {
            title = parts[0];
            return !string.IsNullOrWhiteSpace(title);
        }

        artist = parts[0];
        title = string.Join(" - ", parts.Skip(1));
        return !string.IsNullOrWhiteSpace(title);
    }

    private static void TryGetMusicBeeSmtcData(ref string title, ref string artist, ref GlobalSystemMediaTransportControlsSessionPlaybackStatus playbackStatus, ref IRandomAccessStreamReference? thumbnail)
    {
        try
        {
            var manager = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
            var musicBeeSession = manager.GetSessions().FirstOrDefault(s =>
                s.SourceAppUserModelId?.Contains("musicbee", StringComparison.OrdinalIgnoreCase) == true);

            if (musicBeeSession == null)
                return;

            var smtcPlayback = musicBeeSession.GetPlaybackInfo();
            playbackStatus = smtcPlayback.PlaybackStatus;

            var mediaProperties = musicBeeSession.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult();
            if (mediaProperties == null)
                return;

            if (!string.IsNullOrWhiteSpace(mediaProperties.Title))
                title = mediaProperties.Title;

            if (!string.IsNullOrWhiteSpace(mediaProperties.Artist))
                artist = mediaProperties.Artist;

            thumbnail = mediaProperties.Thumbnail;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "MusicBee SMTC snapshot unavailable, using window-title fallback");
        }
    }
}