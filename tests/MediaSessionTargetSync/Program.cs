using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

var failures = 0;
var tests = new (string Name, Action Run)[]
{
    ("manual selection refreshes a paused player's position", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        Equal(spotify.Id, window.FlyoutTarget);
        Equal(spotify.Position, window.Position);
        Equal(spotify.Status, window.Status);
    }),
    ("Automatic refreshes the system target and paused position", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        window.PinSession(null);
        Equal(edge.Id, window.TaskbarTarget);
        Equal(edge.Id, window.FlyoutTarget);
        Equal(edge.Position, window.Position);
    }),
    ("system focus refreshes a hidden taskbar without showing the flyout", () =>
    {
        var (window, manager, spotify, edge) = Create(false);
        manager.Focus(spotify);
        window.Dispatcher.Drain();
        Equal(spotify.Id, window.TaskbarTarget);
        Equal(false, window.IsVisible);
    }),
    ("system focus refreshes visible metadata, playback and position", () =>
    {
        var (window, manager, spotify, edge) = Create();
        manager.Focus(spotify);
        window.Dispatcher.Drain();
        Equal(spotify.Id, window.FlyoutTarget);
        Equal(spotify.Position, window.Position);
        Equal(spotify.Status, window.Status);
    }),
    ("opening a session refreshes the previously empty target", () =>
    {
        SettingsManager.Current.PinnedSessionId = "";
        var manager = new MediaManager();
        var window = new MainWindow(manager, false);
        var session = NewSession("Spotify", 30);
        manager.Open(session);
        window.Dispatcher.Drain();
        Equal(session.Id, window.TaskbarTarget);
    }),
    ("closing the pinned player refreshes the visible fallback", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        manager.Close(spotify);
        window.Dispatcher.Drain();
        Equal("", SettingsManager.Current.PinnedSessionId);
        Equal(edge.Id, window.TaskbarTarget);
        Equal(edge.Id, window.FlyoutTarget);
        Equal(edge.Position, window.Position);
    }),
    ("closing the last player hides stale content and stops progress", () =>
    {
        var (window, manager, spotify, edge) = Create();
        manager.Close(spotify);
        manager.Close(edge);
        window.Dispatcher.Drain();
        Equal(null, window.TaskbarTarget);
        Equal(false, window.IsVisible);
        Equal(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed, window.Status);
    }),
    ("a queued focus callback reads the latest manual selection", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.Dispatcher.OnUiThread = false;
        manager.Focus(edge);
        window.Dispatcher.OnUiThread = true;
        window.PinSession(spotify.Id);
        window.Dispatcher.Drain();
        Equal(spotify.Id, window.TaskbarTarget);
        Equal(spotify.Id, window.FlyoutTarget);
        Equal(spotify.Position, window.Position);
    }),
    ("a background close does not clear a later manual choice", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        window.Dispatcher.OnUiThread = false;
        manager.Close(spotify);
        window.Dispatcher.OnUiThread = true;
        window.PinSession(edge.Id);
        window.Dispatcher.Drain();
        Equal(edge.Id, SettingsManager.Current.PinnedSessionId);
        Equal(edge.Id, window.FlyoutTarget);
    }),
    ("queued callbacks do not touch UI after cleanup", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.Dispatcher.OnUiThread = false;
        manager.Focus(spotify);
        window.Dispatcher.OnUiThread = true;
        window.Cleanup();
        var updates = window.Updates;
        window.Dispatcher.Drain();
        Equal(updates, window.Updates);
        manager.Focus(edge);
        manager.Open(spotify);
        manager.Close(spotify);
        window.Dispatcher.Drain();
        Equal(updates, window.Updates);
    }),
    ("focus changes preserve a manual selection", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        manager.Focus(edge);
        window.Dispatcher.Drain();
        Equal(spotify.Id, SettingsManager.Current.PinnedSessionId);
        Equal(spotify.Id, window.TaskbarTarget);
    }),
    ("one refresh uses the same target even if Windows focus changes during it", () =>
    {
        var (window, manager, spotify, edge) = Create();
        manager.FocusSequence.Enqueue(spotify);
        manager.FocusSequence.Enqueue(edge);
        window.RefreshFilteredMedia();
        Equal(spotify.Id, window.TaskbarTarget);
        Equal(spotify.Id, window.FlyoutTarget);
    }),
};

foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static MediaSession NewSession(string id, int seconds) => new(id, TimeSpan.FromSeconds(seconds));
static (MainWindow, MediaManager, MediaSession, MediaSession) Create(bool visible = true)
{
    SettingsManager.Current.PinnedSessionId = "";
    var manager = new MediaManager();
    var spotify = NewSession("Spotify", 30);
    var edge = NewSession("Edge", 120);
    manager.CurrentMediaSessions.Add(spotify.Id, spotify);
    manager.CurrentMediaSessions.Add(edge.Id, edge);
    manager.Focused = edge;
    return (new MainWindow(manager, visible), manager, spotify, edge);
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"expected {expected}, actual {actual}");
}

namespace FluentFlyoutWPF
{
    public partial class MainWindow
    {
        public readonly MediaManager mediaManager;
        public readonly TestDispatcher Dispatcher = new();
        private bool _isCleaningUp;
        private bool _isHiding { get; set; }
        private bool _seekBarEnabled = true;
        private bool _isDragging { get; set; }
        private DateTime _lastSelfUpdateTimestamp;
        private readonly CancellationTokenSource cts = new();
        private static readonly TestLogger Logger = new();
        public bool IsVisible;
        public string? TaskbarTarget;
        public string? FlyoutTarget;
        public TimeSpan Position;
        public GlobalSystemMediaTransportControlsSessionPlaybackStatus? Status;
        public int Updates;
        public MainWindow(MediaManager manager, bool visible)
        {
            mediaManager = manager;
            IsVisible = visible;
            AttachMediaEvents();
        }
        public bool IsSessionAllowed(MediaSession session) => true;
        public void Cleanup() { _isCleaningUp = true; DetachMediaEvents(); }
        private void AssertUi() { if (!Dispatcher.CheckAccess()) throw new Exception("UI accessed outside Dispatcher"); Updates++; }
        private void UpdateTaskbar() => UpdateTaskbar(GetActiveMediaSession());
        private void UpdateTaskbar(MediaSession? session) { AssertUi(); TaskbarTarget = session?.Id; }
        private void UpdateUI(MediaSession session) { AssertUi(); ArgumentNullException.ThrowIfNull(session); FlyoutTarget = session.Id; }
        private void HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status) { AssertUi(); Status = status; }
        private void UpdateSeekbarCurrentDuration(TimeSpan position) { AssertUi(); Position = position; }
        private void Hide() { AssertUi(); IsVisible = false; }
    }
    public sealed class TestLogger { public void Debug(string message) { } }
    public sealed class TestDispatcher
    {
        private readonly Queue<Action> pending = new();
        public bool OnUiThread = true;
        public bool CheckAccess() => OnUiThread;
        public void BeginInvoke(Action callback) => pending.Enqueue(callback);
        public void Drain() { OnUiThread = true; while (pending.TryDequeue(out var callback)) callback(); }
    }
}
namespace FluentFlyout.Classes.Settings
{
    public static class SettingsManager { public static readonly TestSettings Current = new(); }
    public sealed class TestSettings { public string PinnedSessionId = ""; }
}
namespace Windows.Media.Control
{
    public enum GlobalSystemMediaTransportControlsSessionPlaybackStatus { Closed, Paused, Playing }
}
namespace WindowsMediaController
{
    public sealed class MediaManager
    {
        public readonly Dictionary<string, MediaSession> CurrentMediaSessions = new();
        public MediaSession? Focused;
        public Queue<MediaSession> FocusSequence = new();
        public event Action<MediaSession>? OnFocusedSessionChanged;
        public event Action<MediaSession>? OnAnySessionOpened;
        public event Action<MediaSession>? OnAnySessionClosed;
        public MediaSession? GetFocusedSession() => FocusSequence.TryDequeue(out var session) ? session : Focused;
        public void Focus(MediaSession session) { Focused = session; OnFocusedSessionChanged?.Invoke(session); }
        public void Open(MediaSession session) { CurrentMediaSessions[session.Id] = session; OnAnySessionOpened?.Invoke(session); }
        public void Close(MediaSession session) { CurrentMediaSessions.Remove(session.Id); OnAnySessionClosed?.Invoke(session); }
        public sealed class MediaSession(string id, TimeSpan position)
        {
            public string Id = id;
            public TimeSpan Position = position;
            public GlobalSystemMediaTransportControlsSessionPlaybackStatus Status = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
            public MediaSession ControlSession => this;
            public PlaybackInfo GetPlaybackInfo() => new(Status);
            public Timeline GetTimelineProperties() => new(Position);
        }
        public sealed record PlaybackInfo(GlobalSystemMediaTransportControlsSessionPlaybackStatus PlaybackStatus);
        public sealed record Timeline(TimeSpan Position);
    }
}
