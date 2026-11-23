// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Utils;
using System.Runtime.InteropServices;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    // time budgets of the launch and resume flow, the worst case stays well below one minute
    private static readonly TimeSpan MediaAppSessionTimeout = TimeSpan.FromSeconds(30); // wait for the app to expose a media session
    private static readonly TimeSpan MediaAppReadyTimeout = TimeSpan.FromSeconds(10); // wait for that session to have a track and accept Play
    private static readonly TimeSpan MediaAppPlayConfirmTimeout = TimeSpan.FromSeconds(4); // wait for an accepted Play request to take effect
    private static readonly TimeSpan MediaAppPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MediaAppConfirmPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MediaAppPlayRetryDelay = TimeSpan.FromMilliseconds(1500);
    private const int MediaAppPlayAttempts = 3;

    private int _mediaAppLaunchInProgress;
    private CancellationTokenSource? _mediaAppLaunchCts;

    private enum ResumeOutcome
    {
        AlreadyPlaying,
        Resumed,
        SessionFiltered,
        SessionClosed,
        BlockedByOtherPlayback,
        CouldNotVerifyOtherPlayback,
        NotResumable,
        NotAccepted,
        NotConfirmed
    }

    private readonly record struct MediaSessionState(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus? Status,
        bool IsPlayEnabled,
        bool HasTrack);

    /// <summary>
    /// Launches a media app and, when it exposes a session with a track that can be played, asks that session to resume.
    /// Never throws: launch failures are shown as a notification, everything else is logged.
    /// </summary>
    internal async Task LaunchMediaAppAndResumeAsync(MediaAppDefinition app)
    {
        // do not start while shutting down, and ignore extra clicks while a previous launch is still waiting
        if (_isCleaningUp || Interlocked.CompareExchange(ref _mediaAppLaunchInProgress, 1, 0) != 0)
            return;

        using CancellationTokenSource launchCts = new();
        _mediaAppLaunchCts = launchCts;

        try
        {
            MediaAppLaunchResult result = MediaAppLauncher.TryLaunch(app);
            if (result != MediaAppLaunchResult.Started)
            {
                Notifications.ShowMediaAppLaunchFailedNotification(app.DisplayName, result);
                return;
            }

            await ResumeMediaAppPlaybackAsync(app, launchCts.Token);
        }
        catch (OperationCanceledException)
        {
            // FluentFlyout is shutting down, CancelMediaAppLaunch already logged it (logging stops with the shutdown)
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to launch and resume {0}", app.DisplayName);
        }
        finally
        {
            _mediaAppLaunchCts = null;
            Interlocked.Exchange(ref _mediaAppLaunchInProgress, 0);
        }
    }

    /// <summary>
    /// Called from <see cref="CleanupResources"/>, stops a pending launch so no polling or delay outlives the app.
    /// </summary>
    private void CancelMediaAppLaunch()
    {
        try
        {
            CancellationTokenSource? launchCts = _mediaAppLaunchCts;
            if (launchCts == null)
                return;

            Logger.Info("Shutdown requested, cancelling the pending media app launch");
            launchCts.Cancel();
        }
        catch (Exception ex)
        {
            // must never interrupt the shutdown sequence
            Logger.Warn(ex, "Failed to cancel the pending media app launch");
        }
    }

    private async Task ResumeMediaAppPlaybackAsync(MediaAppDefinition app, CancellationToken token)
    {
        MediaSession? session = await WaitForMediaAppSessionAsync(app, token);
        if (session == null)
            return;

        ResumeOutcome outcome = await ResumeSessionAsync(session, token);

        // one summary line instead of logging every poll
        Logger.Info("{0} resume finished: {1}, final playback status: {2}", app.DisplayName, outcome,
            TryGetPlaybackStatus(session)?.ToString() ?? "unknown");
    }

    private async Task<MediaSession?> WaitForMediaAppSessionAsync(MediaAppDefinition app, CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow + MediaAppSessionTimeout;
        int sessionCount = 0;
        bool loggedLookalike = false;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            List<MediaSession>? sessions = TrySnapshotSessions();
            if (sessions != null)
            {
                sessionCount = sessions.Count;

                MediaSession? session = sessions.FirstOrDefault(s => IsSessionOfApp(s, app));
                if (session != null)
                {
                    await LogDetectedSessionAsync(app, session);
                    return session;
                }

                // a session that only looks like the app is never controlled, but log it once so the id can be added if it is the app
                MediaSession? lookalike = sessions.FirstOrDefault(s => s.Id?.Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase) == true);
                if (lookalike != null && !loggedLookalike)
                {
                    loggedLookalike = true;
                    Logger.Info("Media session '{0}' resembles {1} but does not match a known session id, it will not be controlled", lookalike.Id, app.DisplayName);
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                Logger.Info("{0} did not expose a media session within {1} s ({2} sessions present), playback was not resumed",
                    app.DisplayName, MediaAppSessionTimeout.TotalSeconds, sessionCount);
                return null;
            }

            await Task.Delay(MediaAppPollInterval, token);
        }
    }

    private async Task<ResumeOutcome> ResumeSessionAsync(MediaSession session, CancellationToken token)
    {
        // Windows reports whether a session supports Play (IsPlayEnabled), so that is the gate, not a guess from the playback status.
        // A session without a track has nothing to resume, in which case no Play request is sent at all.
        DateTime readyDeadline = DateTime.UtcNow + MediaAppReadyTimeout;
        MediaSessionState? state;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            state = await TryReadSessionStateAsync(session);
            if (state == null)
                return ResumeOutcome.SessionClosed;

            if (state.Value.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return ResumeOutcome.AlreadyPlaying;

            if (IsResumable(state.Value))
                break;

            if (DateTime.UtcNow >= readyDeadline)
            {
                Logger.Info("Session is not resumable, nothing was sent: status={0}, playEnabled={1}, hasTrack={2}",
                    state.Value.Status, state.Value.IsPlayEnabled, state.Value.HasTrack);
                return ResumeOutcome.NotResumable;
            }

            await Task.Delay(MediaAppPollInterval, token);
        }

        ResumeOutcome lastFailure = ResumeOutcome.CouldNotVerifyOtherPlayback;

        for (int attempt = 1; attempt <= MediaAppPlayAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();

            // everything below is re-checked before every single Play request
            if (!IsSessionAllowed(session))
                return ResumeOutcome.SessionFiltered;

            bool? competing = TryFindCompetingPlayback(session, out string? blockingSessionId);
            if (competing == true)
            {
                Logger.Info("Another allowed media session is playing ({0}), the app was not resumed", blockingSessionId);
                return ResumeOutcome.BlockedByOtherPlayback;
            }

            if (competing == null)
            {
                // the session list could not be read reliably: never risk a second audio source, try again after a pause
                lastFailure = ResumeOutcome.CouldNotVerifyOtherPlayback;
                if (attempt < MediaAppPlayAttempts)
                    await Task.Delay(MediaAppPlayRetryDelay, token);
                continue;
            }

            state = await TryReadSessionStateAsync(session);
            if (state == null)
                return ResumeOutcome.SessionClosed;

            if (state.Value.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return ResumeOutcome.Resumed;

            if (!IsResumable(state.Value))
                return ResumeOutcome.NotResumable;

            // only ever this one session, and only an explicit Play (never a play/pause toggle)
            GlobalSystemMediaTransportControlsSession? control = session.ControlSession;
            if (control == null)
                return ResumeOutcome.SessionClosed;

            bool accepted;
            try
            {
                accepted = await control.TryPlayAsync();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
            {
                Logger.Warn(ex, "Play request failed");
                accepted = false;
            }

            Logger.Info("Play request {0}/{1} accepted: {2}", attempt, MediaAppPlayAttempts, accepted);

            if (!accepted)
            {
                // refused, the app may still be starting: retry after a pause
                lastFailure = ResumeOutcome.NotAccepted;
                if (attempt < MediaAppPlayAttempts)
                    await Task.Delay(MediaAppPlayRetryDelay, token);
                continue;
            }

            // accepted: do not send anything else, give it time to take effect
            if (await WaitForPlayingAsync(session, token))
                return ResumeOutcome.Resumed;

            // accepted but nothing happened: another attempt is only made if the session still looks resumable (checked above)
            lastFailure = ResumeOutcome.NotConfirmed;
        }

        return lastFailure;
    }

    private static async Task<bool> WaitForPlayingAsync(MediaSession session, CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow + MediaAppPlayConfirmTimeout;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            if (TryGetPlaybackStatus(session) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return true;

            if (DateTime.UtcNow >= deadline)
                return false;

            await Task.Delay(MediaAppConfirmPollInterval, token);
        }
    }

    /// <summary>
    /// Looks for another media session the user allows that is playing right now.
    /// Returns true when one is playing, false when none is, and null when that could not be determined.
    /// </summary>
    private bool? TryFindCompetingPlayback(MediaSession target, out string? blockingSessionId)
    {
        blockingSessionId = null;

        List<MediaSession>? sessions = TrySnapshotSessions();
        if (sessions == null)
            return null;

        foreach (MediaSession other in sessions)
        {
            if (ReferenceEquals(other, target) || string.Equals(other.Id, target.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            // sessions excluded by the app filtering settings are not part of the user's playback
            if (!IsSessionAllowed(other))
                continue;

            // Without a control session, playback state cannot be verified safely.
            if (other.ControlSession == null)
                return null;

            GlobalSystemMediaTransportControlsSessionPlaybackStatus? status = TryGetPlaybackStatus(other);
            if (status == null)
                return null;

            if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                blockingSessionId = other.Id;
                return true;
            }
        }

        return false;
    }

    private List<MediaSession>? TrySnapshotSessions()
    {
        // the media manager updates its session list while it is read here, so copying it can fail halfway
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return mediaManager.CurrentMediaSessions.Values.ToList();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // the list changed while copying, try again
            }
        }

        return null;
    }

    private static async Task<MediaSessionState?> TryReadSessionStateAsync(MediaSession session)
    {
        GlobalSystemMediaTransportControlsSession? control = session.ControlSession;
        if (control == null)
            return null;

        try
        {
            GlobalSystemMediaTransportControlsSessionPlaybackInfo? info = control.GetPlaybackInfo();
            GlobalSystemMediaTransportControlsSessionMediaProperties? properties = await control.TryGetMediaPropertiesAsync();

            // only whether a track is present is recorded, titles and artists are never logged
            bool hasTrack = properties != null
                && !string.IsNullOrWhiteSpace(properties.Title);

            return new MediaSessionState(info?.PlaybackStatus, info?.Controls?.IsPlayEnabled ?? false, hasTrack);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // the session closed while it was being read
            return null;
        }
    }

    private static GlobalSystemMediaTransportControlsSessionPlaybackStatus? TryGetPlaybackStatus(MediaSession session)
    {
        try
        {
            return session.ControlSession?.GetPlaybackInfo()?.PlaybackStatus;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static bool IsResumable(MediaSessionState state)
    {
        return state.IsPlayEnabled
            && state.HasTrack
            && state.Status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing;
    }

    private static bool IsSessionOfApp(MediaSession session, MediaAppDefinition app)
    {
        string sessionId = session.Id ?? string.Empty;

        return app.SessionIdPrefixes.Any(prefix =>
            !string.IsNullOrEmpty(prefix) && sessionId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private async Task LogDetectedSessionAsync(MediaAppDefinition app, MediaSession session)
    {
        MediaSessionState? state = await TryReadSessionStateAsync(session);

        string playerName;
        try
        {
            playerName = MediaPlayerData.GetAndCacheMediaPlayerData(session.Id ?? string.Empty).Item1;
        }
        catch (Exception)
        {
            playerName = "unavailable";
        }

        Logger.Info("Detected {0} media session: id={1}, player={2}, allowedByFilter={3}, status={4}, playEnabled={5}, hasTrack={6}",
            app.DisplayName, session.Id, playerName, IsSessionAllowed(session),
            state?.Status?.ToString() ?? "unknown", state?.IsPlayEnabled, state?.HasTrack);
    }
}