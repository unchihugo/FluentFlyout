// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    private readonly Lock _sessionPlaybackStateLock = new();
    private readonly Dictionary<MediaSession, bool> _sessionPlaybackStates = [];

    private void CaptureSessionPlaybackStates()
    {
        foreach (var session in mediaManager.CurrentMediaSessions.Values)
        {
            if (GetSessionPlayingState(session) is { } isPlaying)
                _sessionPlaybackStates[session] = isPlaying;
        }
    }

    private static bool? GetSessionPlayingState(MediaSession mediaSession)
    {
        try
        {
            return mediaSession.ControlSession?.GetPlaybackInfo()?.PlaybackStatus is { } status
                ? status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                : null;
        }
        catch (COMException)
        {
            // A session can disappear during sampling; an unreadable state is not a playback transition.
            return null;
        }
    }

    private void RestoreSystemFollowOnNewPlayback(MediaSession mediaSession)
    {
        lock (_sessionPlaybackStateLock)
        {
            // Read current state under the lock so a delayed event snapshot cannot invent a playback transition.
            if (GetSessionPlayingState(mediaSession) is not { } isPlaying)
                return;

            bool wasPlaying = _sessionPlaybackStates.TryGetValue(mediaSession, out bool previous) && previous;
            _sessionPlaybackStates[mediaSession] = isPlaying;

            string pinnedId = SettingsManager.Current.PinnedSessionId;
            if (!isPlaying || wasPlaying ||
                !SettingsManager.Current.MediaSessionAutoFollowEnabled ||
                string.IsNullOrEmpty(pinnedId) ||
                mediaSession.Id == pinnedId ||
                !IsSessionAllowed(mediaSession))
                return;

            SettingsManager.Current.PinnedSessionId = string.Empty;
        }
    }

    private void ForgetSessionPlaybackState(MediaSession mediaSession)
    {
        lock (_sessionPlaybackStateLock)
            _sessionPlaybackStates.Remove(mediaSession);
    }

    public IReadOnlyList<MediaSession> GetSwitchableSessions()
    {
        return mediaManager.CurrentMediaSessions.Values.Where(IsSessionAllowed).ToList();
    }

    public void PinSession(string? sessionId)
    {
        lock (_sessionPlaybackStateLock)
        {
            if (!string.IsNullOrEmpty(sessionId))
                CaptureSessionPlaybackStates();
            SettingsManager.Current.PinnedSessionId = sessionId ?? string.Empty;
        }
        RefreshFilteredMedia();
    }

    private void ShowSessionSwitcherMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = MediaIdButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top
        };

        PopulateSessionSwitcherMenu(menu);
        menu.IsOpen = true;
    }

    internal void PopulateSessionSwitcherMenu(System.Windows.Controls.ItemsControl menu)
    {
        var sessions = GetSwitchableSessions();
        string pinnedId = SettingsManager.Current.PinnedSessionId;
        string autoText = TryFindResource("SessionSwitcherAuto") as string ?? string.Empty;

        // Clear existing items and events
        foreach (var item in menu.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            item.Click -= SessionMenuItem_Click;
        }
        menu.Items.Clear();

        var autoItem = new System.Windows.Controls.MenuItem
        {
            Header = autoText,
            IsCheckable = true,
            IsChecked = string.IsNullOrEmpty(pinnedId),
            Tag = string.Empty
        };
        autoItem.Click += SessionMenuItem_Click;
        menu.Items.Add(autoItem);
        menu.Items.Add(new System.Windows.Controls.Separator());

        foreach (var session in sessions)
        {
            string sessionId = session.Id ?? string.Empty;
            (string appName, ImageSource? appIcon) = MediaPlayerData.GetAndCacheMediaPlayerData(sessionId);

            string trackLabel = string.Empty;
            bool isPlaying = false;
            try
            {
                if (session.ControlSession != null)
                {
                    isPlaying = session.ControlSession.GetPlaybackInfo()?.PlaybackStatus
                        == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    var props = TryGetMediaProperties(session.ControlSession);
                    if (props != null && (!string.IsNullOrWhiteSpace(props.Title) || !string.IsNullOrWhiteSpace(props.Artist)))
                        trackLabel = string.IsNullOrWhiteSpace(props.Artist) ? props.Title : $"{props.Title} - {props.Artist}";
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to read properties for session {SessionId}", sessionId);
            }

            var item = new System.Windows.Controls.MenuItem
            {
                Header = appName,
                ToolTip = string.IsNullOrEmpty(trackLabel) ? sessionId : $"{trackLabel}\n{sessionId}",
                IsCheckable = true,
                IsChecked = sessionId == pinnedId,
                Tag = sessionId,
                FontWeight = isPlaying ? FontWeights.Bold : FontWeights.Normal
            };

            if (appIcon != null)
            {
                item.Icon = new System.Windows.Controls.Image
                {
                    Source = appIcon,
                    Width = 16,
                    Height = 16
                };
            }

            item.Click += SessionMenuItem_Click;
            menu.Items.Add(item);
        }
    }

    private void SessionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { Tag: string sessionId }) return;
        PinSession(string.IsNullOrEmpty(sessionId) ? null : sessionId);
    }
}