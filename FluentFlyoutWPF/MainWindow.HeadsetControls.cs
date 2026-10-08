// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using Windows.Media.Control;
using static FluentFlyout.Classes.NativeMethods;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    private const int LocalInputGracePeriodMs = 500;

    private ExternalVolumeMonitor? _externalVolumeMonitor;
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSessionPlaybackStatus> _lastPlaybackStatuses = [];

    private static bool HeadsetControlsEnabled => SettingsManager.Current.MediaFlyoutHeadsetControlsEnabled;

    private void InitializeHeadsetControls()
    {
        foreach (var session in mediaManager.CurrentMediaSessions.Values)
            UpdateLastPlaybackStatus(session);

        mediaManager.OnAnyPlaybackStateChanged += HeadsetControls_OnPlaybackStateChanged;
        mediaManager.OnAnySessionClosed += HeadsetControls_OnSessionClosed;

        _externalVolumeMonitor = new ExternalVolumeMonitor(LocalInputGracePeriodMs);
        _externalVolumeMonitor.ExternalVolumeChanged += HeadsetControls_OnExternalVolumeChanged;
    }

    private void DisposeHeadsetControls()
    {
        mediaManager.OnAnyPlaybackStateChanged -= HeadsetControls_OnPlaybackStateChanged;
        mediaManager.OnAnySessionClosed -= HeadsetControls_OnSessionClosed;

        if (_externalVolumeMonitor != null)
        {
            _externalVolumeMonitor.ExternalVolumeChanged -= HeadsetControls_OnExternalVolumeChanged;
            _externalVolumeMonitor.Dispose();
            _externalVolumeMonitor = null;
        }
    }

    private static bool IsHeadsetAppCommand(int device)
    {
        if (device != FAPPCOMMAND_OEM)
            return false;

#if DEBUG
        Logger.Debug("App command received from OEM device, headset controls enabled: " + HeadsetControlsEnabled);
#endif
        return HeadsetControlsEnabled;
    }

    private void HeadsetControls_OnExternalVolumeChanged(object? sender, EventArgs e)
    {
        if (!HeadsetControlsEnabled)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!SettingsManager.Current.MediaFlyoutVolumeKeysExcluded)
                TryShowMediaFlyoutDebounced();

            ShowVolumeFlyout();
        });
    }

    private void HeadsetControls_OnPlaybackStateChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null)
    {
        var (previousStatus, currentStatus) = UpdateLastPlaybackStatus(mediaSession, playbackInfo);

        if (!HeadsetControlsEnabled)
            return;

        bool isPlayPauseToggle =
            (previousStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && currentStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) ||
            (previousStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused && currentStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);

        if (!isPlayPauseToggle || LocalInputDetector.HadInputWithin(LocalInputGracePeriodMs))
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (GetActiveMediaSession()?.Id != mediaSession.Id)
                return;

#if DEBUG
            Logger.Debug("Play/pause without key press detected: " + mediaSession.Id + " " + currentStatus);
#endif
            TryShowMediaFlyoutDebounced();
        });
    }

    private void HeadsetControls_OnSessionClosed(MediaSession mediaSession)
    {
        lock (_lastPlaybackStatuses)
            _lastPlaybackStatuses.Remove(mediaSession.Id);
    }

    private (GlobalSystemMediaTransportControlsSessionPlaybackStatus? Previous, GlobalSystemMediaTransportControlsSessionPlaybackStatus? Current)
        UpdateLastPlaybackStatus(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null)
    {
        // sometimes mediaSession.ControlSession can be null
        var currentStatus = playbackInfo?.PlaybackStatus ?? mediaSession.ControlSession?.GetPlaybackInfo()?.PlaybackStatus;
        if (currentStatus == null)
            return (null, null);

        lock (_lastPlaybackStatuses)
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus? previousStatus =
                _lastPlaybackStatuses.TryGetValue(mediaSession.Id, out var lastStatus) ? lastStatus : null;

            _lastPlaybackStatuses[mediaSession.Id] = currentStatus.Value;
            return (previousStatus, currentStatus);
        }
    }
}