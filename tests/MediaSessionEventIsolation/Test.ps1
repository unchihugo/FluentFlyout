param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '../../FluentFlyoutWPF/MainWindow.xaml.cs')
)

$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath $SourcePath -Raw
$start = $source.IndexOf('    private void pauseOtherMediaSessionsIfNeeded(')
$end = $source.IndexOf('    private void MediaManager_OnAnyTimelinePropertyChanged(', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Media event handlers not found.' }
$handlers = $source.Substring($start, $end - $start)

# Execute the production callbacks; fake only SMTC and UI dependencies, without starting the app.
$fixture = @'
#nullable enable
using System;
using Status = GlobalSystemMediaTransportControlsSessionPlaybackStatus;

enum GlobalSystemMediaTransportControlsSessionPlaybackStatus { Closed, Playing, Paused }
class GlobalSystemMediaTransportControlsSessionPlaybackInfo
{
    public Status PlaybackStatus;
    public object? Controls = new();
}
class GlobalSystemMediaTransportControlsSessionMediaProperties
{
    public string Title = "Same song", Artist = "Same artist";
    public object Thumbnail = 1;
}
class ControlSession
{
    public GlobalSystemMediaTransportControlsSessionPlaybackInfo Playback = new();
    public GlobalSystemMediaTransportControlsSessionMediaProperties Properties = new();
    public GlobalSystemMediaTransportControlsSessionPlaybackInfo GetPlaybackInfo() => Playback;
}
class MediaSession
{
    public string Id;
    public ControlSession ControlSession = new();
    public MediaSession(string id, Status status) { Id = id; ControlSession.Playback.PlaybackStatus = status; }
}
static class SettingsManager
{
    public static Settings Current = new();
    public class Settings { public bool PauseOtherSessionsEnabled = true, NextUpEnabled = false; }
}
static class BitmapHelper
{
    public static object GetThumbnail(object thumbnail) => thumbnail;
    public static int GetStableThumbnailHash(object thumbnail) => thumbnail.GetHashCode();
    public static void GetDominantColors(int count) { }
}
static class FullscreenDetector { public static bool IsFullscreenApplicationRunning() => false; }
static class WindowHelper { public static void SetVisibility(NextUpWindow window, bool visible) { } }
class NextUpWindow
{
    public NextUpWindow(string title, string artist, object thumbnail) { }
    public event EventHandler? Closed;
    public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    public void UpdateThumbnail(object thumbnail) { }
}
class FakeDispatcher { public void Invoke(Action action) => action(); }
class FakeTaskbar
{
    public int Updates;
    public void UpdateUi(string title, string artist, object? thumbnail, Status? status, object? controls = null) => Updates++;
}
public class MediaSessionEventIsolationTests
{
    private MediaSession? active;
    private FakeTaskbar taskbarWindow = new();
    private bool IsVisible = true;
    private NextUpWindow? nextUpWindow;
    private string currentTitle = "";
    private FakeDispatcher Dispatcher = new();
    private int uiUpdates, pauseCalls;
    private Status? lastPlaybackState;
    private MediaSession? GetActiveMediaSession() => active;
    private GlobalSystemMediaTransportControlsSessionMediaProperties TryGetMediaProperties(ControlSession session) => session.Properties;
    private void PauseOtherSessions(MediaSession session) => pauseCalls++;
    private void UpdateUI(MediaSession session) => uiUpdates++;
    private void HandlePlayBackState(Status? status) => lastPlaybackState = status;

__HANDLERS__

    public static void Run()
    {
        int failures = 0;
        void Check(string name, Action<MediaSessionEventIsolationTests> test)
        {
            try { test(new()); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        Check("background playback cannot update the selected player's UI", t =>
        {
            t.active = new("Spotify", Status.Playing);
            t.CurrentSession_OnPlaybackStateChanged(new("Edge", Status.Paused), new() { PlaybackStatus = Status.Paused });
            Equal(0, t.uiUpdates); Equal(0, t.taskbarWindow.Updates); Equal<Status?>(null, t.lastPlaybackState);
        });
        Check("background playback still invokes pause-other-sessions", t =>
        {
            t.active = new("Spotify", Status.Paused);
            t.CurrentSession_OnPlaybackStateChanged(new("Edge", Status.Playing), new() { PlaybackStatus = Status.Playing });
            Equal(1, t.pauseCalls); Equal(0, t.uiUpdates);
        });
        Check("selected playback uses the selected session's current state", t =>
        {
            t.active = new("Spotify", Status.Playing);
            t.CurrentSession_OnPlaybackStateChanged(t.active, new() { PlaybackStatus = Status.Paused });
            Equal(1, t.uiUpdates); Equal(1, t.taskbarWindow.Updates); Equal<Status?>(Status.Playing, t.lastPlaybackState);
        });
        Check("background metadata cannot update the selected player's UI", t =>
        {
            t.active = new("Spotify", Status.Playing);
            var other = new MediaSession("Edge", Status.Paused);
            t.MediaManager_OnAnyMediaPropertyChanged(other, other.ControlSession.Properties);
            Equal(0, t.uiUpdates); Equal(0, t.taskbarWindow.Updates);
        });
        Check("background metadata still invokes pause-other-sessions after duplicate metadata", t =>
        {
            t.active = new("Spotify", Status.Playing);
            t.MediaManager_OnAnyMediaPropertyChanged(t.active, t.active.ControlSession.Properties);
            var other = new MediaSession("Edge", Status.Playing);
            t.MediaManager_OnAnyMediaPropertyChanged(other, other.ControlSession.Properties);
            Equal(2, t.pauseCalls); Equal(1, t.uiUpdates);
        });
        Check("identical metadata from a newly selected player is not deduplicated", t =>
        {
            t.active = new("Spotify", Status.Playing);
            t.MediaManager_OnAnyMediaPropertyChanged(t.active, t.active.ControlSession.Properties);
            t.active = new("Edge", Status.Playing);
            t.MediaManager_OnAnyMediaPropertyChanged(t.active, t.active.ControlSession.Properties);
            Equal(2, t.uiUpdates); Equal(2, t.taskbarWindow.Updates);
        });
        Check("duplicate selected metadata still invokes pause-other-sessions", t =>
        {
            t.active = new("Spotify", Status.Playing);
            t.MediaManager_OnAnyMediaPropertyChanged(t.active, t.active.ControlSession.Properties);
            t.MediaManager_OnAnyMediaPropertyChanged(t.active, t.active.ControlSession.Properties);
            Equal(2, t.pauseCalls); Equal(1, t.uiUpdates);
        });
        Check("no active player clears the taskbar", t =>
        {
            t.CurrentSession_OnPlaybackStateChanged(new("Edge", Status.Paused));
            Equal(1, t.taskbarWindow.Updates); Equal(0, t.uiUpdates);
        });
        if (failures != 0) throw new Exception(failures + " regression check(s) failed.");
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new Exception($"expected {expected}, got {actual}");
    }
}
'@

Add-Type -TypeDefinition $fixture.Replace('__HANDLERS__', $handlers) -WarningAction SilentlyContinue
[MediaSessionEventIsolationTests]::Run()
