// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    private bool _hasMultipleMediaSessions;
    private bool _isMediaSessionMenuOpen;
    private bool _mediaSessionTogglePointerDown;
    private string? _selectedMediaSessionId;

    private sealed record MediaSessionMenuSelection(
        MediaSession Session,
        GlobalSystemMediaTransportControlsSessionMediaProperties? MediaProperties);

    private List<MediaSession> GetAllowedMediaSessions()
    {
        return mediaManager.CurrentMediaSessions.Values.Where(IsSessionAllowed).ToList();
    }

    public MediaSession? GetActiveMediaSession()
    {
        var validSessions = GetAllowedMediaSessions();

        if (validSessions.Count == 0) return null;

        if (!SettingsManager.Current.MediaSessionSwitchingEnabled)
        {
            _selectedMediaSessionId = null;
        }
        else if (_selectedMediaSessionId != null)
        {
            var selectedSession = validSessions.FirstOrDefault(session => session.Id == _selectedMediaSessionId);
            if (selectedSession != null)
                return selectedSession;

            // The manually selected session was closed or filtered out. Return to Windows' automatic choice.
            _selectedMediaSessionId = null;
        }

        var focused = mediaManager.GetFocusedSession();
        if (focused != null && validSessions.Any(session => session.Id == focused.Id))
            return focused;

        return validSessions.FirstOrDefault();
    }

    private void FollowNewlyPlayingMediaSession(
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

        Logger.Info($"Following newly playing media session: {mediaSession.Id}");
        _selectedMediaSessionId = mediaSession.Id;
    }

    private void UpdateMediaSessionSelectorVisibility(bool hasMultipleMediaSessions)
    {
        hasMultipleMediaSessions &= SettingsManager.Current.MediaSessionSwitchingEnabled;
        bool compactLayout = SettingsManager.Current.CompactLayout;
        bool showPlayerInfo = SettingsManager.Current.PlayerInfoEnabled && !compactLayout;

        MediaIdButton.Visibility = showPlayerInfo && !hasMultipleMediaSessions
            ? Visibility.Visible
            : Visibility.Collapsed;
        MediaSessionSplitButton.Visibility = showPlayerInfo && hasMultipleMediaSessions
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactMediaSessionButton.Visibility = hasMultipleMediaSessions &&
                                               (compactLayout || !SettingsManager.Current.PlayerInfoEnabled)
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        var entries = new List<(MediaSession Session, GlobalSystemMediaTransportControlsSessionMediaProperties? MediaProperties, string AppName, ImageSource? Icon, string Title, bool IsPlaying)>();

        foreach (var session in GetAllowedMediaSessions())
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

        foreach (var entry in entries
                     .OrderByDescending(entry => entry.Session.Id == (_selectedMediaSessionId ?? focusedSessionId))
                     .ThenByDescending(entry => entry.IsPlaying)
                     .ThenBy(entry => entry.AppName, StringComparer.CurrentCultureIgnoreCase))
        {
            var sessionItem = new Wpf.Ui.Controls.MenuItem
            {
                Header = CreateMediaSessionMenuHeader(entry.AppName, entry.Title, entry.IsPlaying),
                Icon = CreateMediaPlayerIcon(entry.Icon),
                Tag = new MediaSessionMenuSelection(entry.Session, entry.MediaProperties),
                IsCheckable = true,
                IsChecked = entry.Session.Id == focusedSessionId
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

        var header = new StackPanel
        {
            Width = 210,
            Margin = new Thickness(0, 2, 0, 2)
        };
        header.Children.Add(new TextBlock
        {
            Text = appName,
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

    private void SelectMediaSession(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionMediaProperties? mediaProperties)
    {
        if (!SettingsManager.Current.MediaSessionSwitchingEnabled ||
            !GetAllowedMediaSessions().Any(allowedSession => allowedSession.Id == session.Id))
            return;

        _selectedMediaSessionId = session.Id;
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;

        Logger.Info($"Selected media session: {session.Id}");

        mediaProperties ??= TryGetMediaProperties(activeSession.ControlSession);
        UpdateTaskbar(activeSession, mediaProperties);
        if (!IsVisible) return;

        UpdateUI(activeSession, mediaProperties);
        HandlePlayBackState(activeSession.ControlSession.GetPlaybackInfo()?.PlaybackStatus);
    }

    private static GlobalSystemMediaTransportControlsSessionMediaProperties? TryGetMediaProperties(
        GlobalSystemMediaTransportControlsSession controlSession)
    {
        try
        {
            return controlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        }
        catch (COMException ex)
        {
            Logger.Error(ex, "Failed to retrieve data from the player");
            return null;
        }
    }

    private void MediaIdButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SettingsManager.Current.PlayerInfoEnabled || SettingsManager.Current.CompactLayout) return;
        e.Handled = true;
        _ = TryOpenMediaPlayerAsync();
    }

    private void MediaSessionSplitButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mediaSessionTogglePointerDown =
            MediaSessionSplitButton.Template.FindName("PART_Toggle", MediaSessionSplitButton) is FrameworkElement { IsMouseOver: true };
    }

    private void MediaSessionSplitButton_Click(object sender, RoutedEventArgs e)
    {
        bool toggleIsPointerOver =
            MediaSessionSplitButton.Template.FindName("PART_Toggle", MediaSessionSplitButton) is FrameworkElement { IsMouseOver: true };

        // A nested ToggleButton can also make the outer Button produce its own Click event.
        // Use the pointer-down hit region in addition to the routed-event source so the
        // drop-down half never activates the media player.
        if (!ReferenceEquals(e.Source, sender) || _mediaSessionTogglePointerDown || toggleIsPointerOver)
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(() => _mediaSessionTogglePointerDown = false, DispatcherPriority.Input);
            return;
        }

        _mediaSessionTogglePointerDown = false;
        if (!SettingsManager.Current.PlayerInfoEnabled || SettingsManager.Current.CompactLayout) return;
        e.Handled = true;
        _ = TryOpenMediaPlayerAsync();
    }
}