using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using System.Runtime.InteropServices;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;
using Status = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

var failures = 0;
var tests = new (string Name, Action Run)[]
{
    ("repeated Playing preserves a new manual choice", () =>
    {
        var (window, manager, spotify, edge) = Create(Status.Playing);
        window.PinSession(spotify.Id);
        window.Observe(edge);
        window.Observe(edge);
        Pinned(spotify.Id);
    }),
    ("Paused to Playing restores system following once", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        edge.Status = Status.Playing;
        window.Observe(edge);
        Pinned("");
        Equal("", window.SelectionWhenPausing);
        Equal(1, SettingsManager.Current.Releases);
        window.Observe(edge);
        Equal(1, SettingsManager.Current.Releases);
    }),
    ("disabled option preserves selection and still observes playback", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        SettingsManager.Current.MediaSessionAutoFollowEnabled = false;
        edge.Status = Status.Playing;
        window.Observe(edge);
        Pinned(spotify.Id);
        SettingsManager.Current.MediaSessionAutoFollowEnabled = true;
        window.Observe(edge);
        Pinned(spotify.Id);
        edge.Status = Status.Paused;
        window.Observe(edge);
        edge.Status = Status.Playing;
        window.Observe(edge);
        Pinned("");
    }),
    ("Automatic stays automatic", () =>
    {
        var (window, manager, spotify, edge) = Create();
        edge.Status = Status.Playing;
        window.Observe(edge);
        Pinned("");
        Equal(0, SettingsManager.Current.Releases);
    }),
    ("the selected player starting playback preserves its selection", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        spotify.Status = Status.Playing;
        window.Observe(spotify);
        Pinned(spotify.Id);
    }),
    ("a paused background notification preserves selection", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        window.Observe(edge);
        Pinned(spotify.Id);
    }),
    ("reselecting after playback began resets the baseline", () =>
    {
        var (window, manager, spotify, edge) = Create();
        edge.Status = Status.Playing; // Native state changed before its callback reached the app.
        window.PinSession(spotify.Id);
        window.Observe(edge);
        Pinned(spotify.Id);
        var vlc = new MediaSession("VLC", Status.Paused);
        manager.CurrentMediaSessions[vlc.Id] = vlc;
        window.PinSession(vlc.Id);
        window.Observe(edge);
        Pinned(vlc.Id);
    }),
    ("startup preserves a saved choice while another player is already Playing", () =>
    {
        var (window, manager, spotify, edge) = Create(Status.Playing, "Spotify");
        window.Observe(edge);
        Pinned(spotify.Id);
    }),
    ("a newly opened Playing session restores following without another event", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        var vlc = new MediaSession("VLC", Status.Playing);
        manager.Open(vlc);
        Pinned("");
        window.Observe(vlc);
        Equal(1, SettingsManager.Current.Releases);
    }),
    ("a newly opened paused session waits until playback starts", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        var vlc = new MediaSession("VLC", Status.Paused);
        manager.Open(vlc);
        Pinned(spotify.Id);
        vlc.Status = Status.Playing;
        window.Observe(vlc);
        Pinned("");
    }),
    ("excluded sessions are observed without taking over", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        edge.Allowed = false;
        edge.Status = Status.Playing;
        window.Observe(edge);
        Pinned(spotify.Id);
        edge.Allowed = true;
        window.Observe(edge);
        Pinned(spotify.Id);
        edge.Status = Status.Paused;
        window.Observe(edge);
        edge.Status = Status.Playing;
        window.Observe(edge);
        Pinned("");
    }),
    ("closing and reopening the same app is a new session", () =>
    {
        var (window, manager, spotify, edge) = Create(Status.Playing);
        window.PinSession(spotify.Id);
        manager.Close(edge);
        manager.Open(new MediaSession(edge.Id, Status.Playing));
        Pinned("");
    }),
    ("missing playback information does not manufacture a new start", () =>
    {
        var (window, manager, spotify, edge) = Create(Status.Playing);
        window.PinSession(spotify.Id);
        edge.Status = null;
        window.Observe(edge);
        Pinned(spotify.Id);
        edge.Status = Status.Playing;
        window.Observe(edge);
        Pinned(spotify.Id);
    }),
    ("a delayed Paused snapshot cannot undo the current Playing baseline", () =>
    {
        var (window, manager, spotify, edge) = Create(Status.Playing);
        window.PinSession(spotify.Id);
        window.Observe(edge, new(Status.Paused));
        window.Observe(edge, new(Status.Playing));
        Pinned(spotify.Id);
    }),
    ("a delayed Playing snapshot cannot start a currently paused player", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        window.Observe(edge, new(Status.Playing));
        Pinned(spotify.Id);
    }),
    ("concurrent duplicate notifications release a selection at most once", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        edge.Status = Status.Playing;
        Parallel.For(0, 64, _ => window.Observe(edge));
        Pinned("");
        Equal(1, SettingsManager.Current.Releases);
    }),
    ("detached session callbacks no longer change selection", () =>
    {
        var (window, manager, spotify, edge) = Create();
        window.PinSession(spotify.Id);
        window.DetachAutoFollow();
        manager.Open(new MediaSession("VLC", Status.Playing));
        Pinned(spotify.Id);
    }),
    ("startup can sample an unrelated unavailable endpoint", () =>
    {
        SettingsManager.Current = new TestSettings { PinnedSessionId = "Spotify" };
        var manager = new MediaManager();
        manager.CurrentMediaSessions["Edge"] = new("Edge", Status.Playing) { ThrowOnRead = true };
        _ = new MainWindow(manager);
        Pinned("Spotify");
    }),
    ("an unavailable endpoint cannot prevent manual or Automatic selection", () =>
    {
        var (window, manager, spotify, edge) = Create(Status.Playing);
        edge.ThrowOnRead = true;
        window.PinSession(spotify.Id);
        Pinned(spotify.Id);
        window.PinSession(null);
        Pinned("");
        window.PinSession(spotify.Id);
        edge.ThrowOnRead = false;
        window.Observe(edge);
        Pinned(spotify.Id);
    }),
    ("unknown information during reselection preserves the known Playing baseline", () =>
    {
        var (window, manager, spotify, edge) = Create(Status.Playing);
        edge.Status = null;
        window.PinSession(spotify.Id);
        edge.Status = Status.Playing;
        window.Observe(edge);
        Pinned(spotify.Id);
    }),
    ("an unavailable callback does not erase the previous Playing state", () =>
    {
        var (window, manager, spotify, edge) = Create(Status.Playing);
        window.PinSession(spotify.Id);
        edge.ThrowOnRead = true;
        window.Observe(edge);
        Pinned(spotify.Id);
        edge.ThrowOnRead = false;
        window.Observe(edge);
        Pinned(spotify.Id);
    }),
    ("a session without a control endpoint is ignored", () =>
    {
        var (window, manager, spotify, edge) = Create(Status.Playing);
        edge.HasEndpoint = false;
        window.PinSession(spotify.Id);
        window.Observe(edge);
        Pinned(spotify.Id);
        edge.HasEndpoint = true;
        window.Observe(edge);
        Pinned(spotify.Id);
    }),
};

foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static (MainWindow, MediaManager, MediaSession, MediaSession) Create(Status edgeStatus = Status.Paused, string pinned = "")
{
    SettingsManager.Current = new TestSettings { PinnedSessionId = pinned };
    var manager = new MediaManager();
    var spotify = new MediaSession("Spotify", Status.Paused);
    var edge = new MediaSession("Edge", edgeStatus);
    manager.CurrentMediaSessions[spotify.Id] = spotify;
    manager.CurrentMediaSessions[edge.Id] = edge;
    return (new MainWindow(manager), manager, spotify, edge);
}
static void Pinned(string expected) => Equal(expected, SettingsManager.Current.PinnedSessionId);
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"expected '{expected}', actual '{actual}'");
}

namespace FluentFlyoutWPF
{
    public partial class MainWindow
    {
        public readonly MediaManager mediaManager;
        public string? SelectionWhenPausing;
        public MainWindow(MediaManager manager) { mediaManager = manager; InitializeAutoFollow(); }
        public bool IsSessionAllowed(MediaSession session) => session.Allowed;
        private void RefreshFilteredMedia() { }
        private void pauseOtherMediaSessionsIfNeeded(MediaSession session) => SelectionWhenPausing = SettingsManager.Current.PinnedSessionId;
    }
}
namespace FluentFlyout.Classes.Settings
{
    public static class SettingsManager { public static TestSettings Current = new(); }
    public sealed class TestSettings
    {
        private string pinned = "";
        public int Releases;
        public bool MediaSessionAutoFollowEnabled = true;
        public string PinnedSessionId
        {
            get => pinned;
            set { if (value == "" && pinned != "") Releases++; pinned = value; }
        }
    }
}
namespace Windows.Media.Control
{
    public enum GlobalSystemMediaTransportControlsSessionPlaybackStatus { Closed, Stopped, Paused, Playing }
    public sealed record GlobalSystemMediaTransportControlsSessionPlaybackInfo(GlobalSystemMediaTransportControlsSessionPlaybackStatus PlaybackStatus);
}
namespace WindowsMediaController
{
    public sealed class MediaManager
    {
        public readonly Dictionary<string, MediaSession> CurrentMediaSessions = new();
        public event Action<MediaSession>? OnAnySessionOpened;
        public event Action<MediaSession>? OnAnySessionClosed;
        public void Open(MediaSession session) { CurrentMediaSessions[session.Id] = session; OnAnySessionOpened?.Invoke(session); }
        public void Close(MediaSession session) { CurrentMediaSessions.Remove(session.Id); OnAnySessionClosed?.Invoke(session); }
        public sealed class MediaSession(string id, Status? status)
        {
            public string Id = id;
            public Status? Status = status;
            public bool Allowed = true;
            public bool ThrowOnRead;
            public bool HasEndpoint = true;
            public MediaSession? ControlSession => HasEndpoint ? this : null;
            public GlobalSystemMediaTransportControlsSessionPlaybackInfo? GetPlaybackInfo()
            {
                if (ThrowOnRead) throw new COMException("Simulated unavailable endpoint", unchecked((int)0x800706BA));
                return Status is { } value ? new(value) : null;
            }
        }
    }
}
