// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    public IReadOnlyList<MediaSession> GetSwitchableSessions()
    {
        return mediaManager.CurrentMediaSessions.Values.Where(IsSessionAllowed).ToList();
    }

    public void PinSession(string? sessionId)
    {
        SettingsManager.Current.PinnedSessionId = sessionId ?? string.Empty;
        RefreshFilteredMedia();
    }

    private void MediaIdButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (!SettingsManager.Current.PlayerInfoEnabled || SettingsManager.Current.CompactLayout) return;
        e.Handled = true;

        if (GetSwitchableSessions().Count >= 2)
        {
            ShowSessionSwitcherMenu();
            return;
        }

        _ = TryOpenMediaPlayerAsync();
    }

    private void ShowSessionSwitcherMenu()
    {
        var sessions = GetSwitchableSessions();
        if (sessions.Count == 0) return;

        string pinnedId = SettingsManager.Current.PinnedSessionId;
        string autoText = TryFindResource("SessionSwitcherAuto") as string ?? string.Empty;
        string openPlayerText = TryFindResource("SessionSwitcherOpenPlayer") as string ?? string.Empty;

        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = MediaIdButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top
        };

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

            string header = string.IsNullOrEmpty(trackLabel) ? appName : $"{appName} ({trackLabel})";

            var item = new System.Windows.Controls.MenuItem
            {
                Header = header,
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

        menu.Items.Add(new System.Windows.Controls.Separator());
        var openItem = new System.Windows.Controls.MenuItem { Header = openPlayerText };
        openItem.Click += (s, e) => _ = TryOpenMediaPlayerAsync();
        menu.Items.Add(openItem);

        menu.IsOpen = true;
    }

    private void SessionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { Tag: string sessionId }) return;
        PinSession(string.IsNullOrEmpty(sessionId) ? null : sessionId);
    }
}