// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    private bool _isMediaSessionMenuOpen;
    private string? _selectedMediaSessionId;

    private sealed record MediaSessionMenuSelection(
        MediaSession Session,
        GlobalSystemMediaTransportControlsSessionMediaProperties? MediaProperties);

    private List<MediaSession> GetAllowedMediaSessions()
    {
        return mediaManager.CurrentMediaSessions.Values.Where(IsSessionAllowed).ToList();
    }

    private MediaSession? GetSystemMediaSession(IReadOnlyList<MediaSession> validSessions)
    {
        if (validSessions.Count == 0) return null;

        var focused = mediaManager.GetFocusedSession();
        return focused != null
            ? validSessions.FirstOrDefault(session => session.Id == focused.Id) ?? validSessions[0]
            : validSessions[0];
    }

    public MediaSession? GetActiveMediaSession()
    {
        var validSessions = GetAllowedMediaSessions();

        if (!SettingsManager.Current.MediaSessionSwitchingEnabled)
            _selectedMediaSessionId = null;

        if (validSessions.Count == 0)
        {
            _selectedMediaSessionId = null;
            return null;
        }

        if (_selectedMediaSessionId != null)
        {
            var selectedSession = validSessions.FirstOrDefault(session => session.Id == _selectedMediaSessionId);
            if (selectedSession != null)
                return selectedSession;

            // The manually selected session was closed or filtered out. Return to Windows' automatic choice.
            _selectedMediaSessionId = null;
        }

        return GetSystemMediaSession(validSessions);
    }

    private void RestoreSystemFollowOnNewPlayback(
        MediaSession mediaSession,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo)
    {
        if (!SettingsManager.Current.MediaSessionSwitchingEnabled ||
            !SettingsManager.Current.MediaSessionAutoFollowEnabled ||
            _selectedMediaSessionId == null ||
            mediaSession.Id == _selectedMediaSessionId ||
            !IsSessionAllowed(mediaSession) ||
            playbackInfo?.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            return;

        Logger.Info($"Restoring system media-session selection after playback started: {mediaSession.Id}");
        _selectedMediaSessionId = null;
    }

    private void MediaManager_OnFocusedSessionChanged(MediaSession mediaSession)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isCleaningUp ||
                (SettingsManager.Current.MediaSessionSwitchingEnabled && _selectedMediaSessionId != null))
                return;

            RefreshSelectedMediaSession();
        });
    }

    private void MediaIdButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (!SettingsManager.Current.MediaSessionSwitchingEnabled ||
            !SettingsManager.Current.PlayerInfoEnabled ||
            SettingsManager.Current.CompactLayout)
            return;

        e.Handled = true;
        ShowSessionSwitcherMenu();
    }

    private void ShowSessionSwitcherMenu()
    {
        var menu = new ContextMenu
        {
            Width = 280,
            PlacementTarget = MediaIdButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            Style = (Style)FindResource("MediaSessionContextMenuStyle")
        };
        menu.Opened += MediaSessionMenu_Opened;
        menu.Closed += MediaSessionMenu_Closed;
        menu.IsOpen = true;
    }

    private void MediaSessionMenu_Opened(object sender, RoutedEventArgs e)
    {
        _isMediaSessionMenuOpen = true;
        if (!_isCleaningUp)
            cts.Cancel();
        if (sender is not ContextMenu menu) return;

        menu.Items.Clear();
        if (!SettingsManager.Current.MediaSessionSwitchingEnabled)
            return;

        string? focusedSessionId = GetActiveMediaSession()?.Id;
        var allowedSessions = GetAllowedMediaSessions();
        var systemSession = GetSystemMediaSession(allowedSessions);
        var entries = new List<(MediaSession Session, GlobalSystemMediaTransportControlsSessionMediaProperties? MediaProperties, string AppName, ImageSource? Icon, string Title, bool IsPlaying)>();

        foreach (var session in allowedSessions)
        {
            try
            {
                (string appName, ImageSource? icon) = MediaPlayerData.GetAndCacheMediaPlayerData(session.Id);
                var mediaProperties = TryGetMediaProperties(session.ControlSession);
                string title = mediaProperties?.Title ?? string.Empty;
                bool isPlaying = session.ControlSession.GetPlaybackInfo().PlaybackStatus ==
                                 GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                entries.Add((session, mediaProperties, appName, icon, title, isPlaying));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to add media session to selector: {session.Id}");
            }
        }

        if (systemSession != null)
        {
            var target = entries.FirstOrDefault(entry => entry.Session.Id == systemSession.Id);
            string appName = target.AppName ?? systemSession.Id;
            string targetName = string.IsNullOrWhiteSpace(target.Title)
                ? appName
                : $"{appName} · {target.Title}";
            string artist = target.MediaProperties?.Artist ?? string.Empty;
            string targetDetails = string.IsNullOrWhiteSpace(artist)
                ? targetName
                : $"{targetName} - {artist}";
            string targetFormat = FindResource("MediaSessionSystemTarget").ToString() ?? "{0}";
            var systemItem = new Wpf.Ui.Controls.MenuItem
            {
                Header = CreateMediaSessionMenuHeader(
                    FindResource("MediaSessionFollowSystem").ToString() ?? string.Empty,
                    string.Format(targetFormat, targetName)),
                Icon = CreateMediaPlayerIcon(target.Icon),
                ToolTip = $"{targetDetails}\n{systemSession.Id}",
                IsCheckable = true,
                IsChecked = _selectedMediaSessionId == null
            };
            systemItem.Click += FollowSystemMenuItem_Click;
            menu.Items.Add(systemItem);
            menu.Items.Add(new Separator());
        }

        foreach (var entry in entries
                     .OrderByDescending(entry => entry.Session.Id == (_selectedMediaSessionId ?? focusedSessionId))
                     .ThenByDescending(entry => entry.IsPlaying)
                     .ThenBy(entry => entry.AppName, StringComparer.CurrentCultureIgnoreCase))
        {
            string artist = entry.MediaProperties?.Artist ?? string.Empty;
            string trackLabel = string.IsNullOrWhiteSpace(entry.Title)
                ? artist
                : string.IsNullOrWhiteSpace(artist) ? entry.Title : $"{entry.Title} - {artist}";
            var sessionItem = new Wpf.Ui.Controls.MenuItem
            {
                Header = CreateMediaSessionMenuHeader(entry.AppName, entry.Title, entry.IsPlaying),
                Icon = CreateMediaPlayerIcon(entry.Icon),
                ToolTip = string.IsNullOrWhiteSpace(trackLabel)
                    ? entry.Session.Id
                    : $"{trackLabel}\n{entry.Session.Id}",
                Tag = new MediaSessionMenuSelection(entry.Session, entry.MediaProperties),
                IsCheckable = true,
                IsChecked = _selectedMediaSessionId != null && entry.Session.Id == focusedSessionId
            };
            sessionItem.Click += MediaSessionMenuItem_Click;
            menu.Items.Add(sessionItem);
        }
    }

    private void MediaSessionMenu_Closed(object sender, RoutedEventArgs e)
    {
        _isMediaSessionMenuOpen = false;
        if (!_isCleaningUp && IsVisible && !SettingsManager.Current.MediaFlyoutAlwaysDisplay)
            ShowMediaFlyout(forceShow: true, refreshUi: false);
    }

    private FrameworkElement CreateMediaSessionMenuHeader(string appName, string title, bool isPlaying)
    {
        string status = FindResource(isPlaying ? "MediaSessionPlaying" : "MediaSessionPaused").ToString() ?? string.Empty;
        string subtitle = string.IsNullOrWhiteSpace(title) ? status : $"{title} · {status}";

        return CreateMediaSessionMenuHeader(appName, subtitle);
    }

    private static FrameworkElement CreateMediaSessionMenuHeader(string title, string subtitle)
    {
        var header = new StackPanel
        {
            Width = 210,
            Margin = new Thickness(0, 2, 0, 2)
        };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        header.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 11,
            Opacity = 0.55,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return header;
    }

    private static Wpf.Ui.Controls.IconElement CreateMediaPlayerIcon(ImageSource? icon)
    {
        if (icon != null)
        {
            return new Wpf.Ui.Controls.ImageIcon
            {
                Source = icon,
                Width = 16,
                Height = 16
            };
        }

        return new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.AppGeneric20, 16, false);
    }

    private void MediaSessionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.MenuItem { Tag: MediaSessionMenuSelection selection })
            SelectMediaSession(selection.Session, selection.MediaProperties);
    }

    private void FollowSystemMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!SettingsManager.Current.MediaSessionSwitchingEnabled) return;

        _selectedMediaSessionId = null;
        Logger.Info("Following Windows' current media session");
        RefreshSelectedMediaSession();
    }

    private void SelectMediaSession(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionMediaProperties? mediaProperties)
    {
        if (!SettingsManager.Current.MediaSessionSwitchingEnabled ||
            !GetAllowedMediaSessions().Any(allowedSession => allowedSession.Id == session.Id))
            return;

        _selectedMediaSessionId = session.Id;
        Logger.Info($"Selected media session: {session.Id}");
        RefreshSelectedMediaSession(mediaProperties);
    }

    private void RefreshSelectedMediaSession(
        GlobalSystemMediaTransportControlsSessionMediaProperties? mediaProperties = null)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null)
        {
            UpdateTaskbar();
            return;
        }

        mediaProperties ??= TryGetMediaProperties(activeSession.ControlSession);
        UpdateTaskbar(activeSession, mediaProperties);
        if (!IsVisible) return;

        UpdateUI(activeSession, mediaProperties);
        HandlePlayBackState(activeSession.ControlSession.GetPlaybackInfo()?.PlaybackStatus);
    }
}