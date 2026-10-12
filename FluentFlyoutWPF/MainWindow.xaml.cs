// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyout.Controls;
using FluentFlyout.Windows;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using FluentFlyoutWPF.Windows;
using MicaWPF.Controls;
using MicaWPF.Core.Extensions;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Windows.ApplicationModel;
using Windows.Media.Control;
using static FluentFlyout.Classes.NativeMethods;
using static FluentFlyoutWPF.Classes.Utils.MonitorUtil;
using static WindowsMediaController.MediaManager;


namespace FluentFlyoutWPF;

public partial class MainWindow : MicaWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private int WM_TASKBARCREATED, WM_SHELLHOOK;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc _hookProc;

    private CancellationTokenSource cts; // to close the flyout after a certain time
    private long _lastFlyoutTime = 0;

    public readonly WindowsMediaController.MediaManager mediaManager = new();

    // for detecting changes in settings (lazy way)
    private int _position = SettingsManager.Current.Position;
    private bool _layout = SettingsManager.Current.CompactLayout;
    private bool _repeatEnabled = SettingsManager.Current.RepeatEnabled;
    private bool _shuffleEnabled = SettingsManager.Current.ShuffleEnabled;
    private bool _playerInfoEnabled = SettingsManager.Current.PlayerInfoEnabled;
    private bool _centerTitleArtist = SettingsManager.Current.CenterTitleArtist;
    private bool _seekBarEnabled = SettingsManager.Current.SeekbarEnabled;
    private bool _alwaysDisplay = SettingsManager.Current.MediaFlyoutAlwaysDisplay;
    private bool _mediaSessionSupportsSeekbar = false; // default off to handle initialization
    private bool _acrylicEnabled = false; // default off to handle initialization
    private int _themeOption = SettingsManager.Current.AppTheme;

    static Mutex singleton = new Mutex(true, "FluentFlyout"); // to prevent multiple instances of the app
    private NextUpWindow? nextUpWindow = null; // to prevent multiple instances of NextUpWindow
    private string currentTitle = ""; // to prevent NextUpWindow from showing the same song

    private readonly int _seekbarUpdateInterval = 300;
    private readonly Timer _positionTimer;
    private bool _isActive;
    private bool _isDragging;
    private bool _isHiding = true;

    private LockWindow? lockWindow;
    private DateTime _lastSelfUpdateTimestamp = DateTime.MinValue;

    internal TaskbarWindow? taskbarWindow;

    private VolumeMixerWindow? volumeMixerWindow;

    internal static volatile bool ExplorerRestarting = false;

    // Feature 3: tracks whether the last media key press was Play/Pause,
    // used to suppress the flyout when the user pauses via keyboard.
    private volatile bool _pauseKeyPressed = false;
    private Timer? _pauseKeyResetTimer;

    public MainWindow()
    {
        DataContext = SettingsManager.Current;
        WindowHelper.SetNoActivate(this); // prevents some fullscreen apps from minimizing
        InitializeComponent();
        WindowHelper.SetTopmost(this); // more prevention of fullscreen apps minimizing

        if (!singleton.WaitOne(TimeSpan.Zero, true)) // if another instance is already running, close this one
        {
            // Signal the existing instance to open settings
            Task.Run(() =>
            {
                try
                {
                    using (EventWaitHandle settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "FluentFlyout_OpenSettings"))
                    {
                        settingsEvent.Set();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to signal existing instance");
                }
            });

            Environment.Exit(0);
        }

        Logger.Info("Starting FluentFlyout MainWindow");

        // in the existing instance, listen for the signal to open settings
        Task.Run(() =>
        {
            try
            {
                using (EventWaitHandle settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "FluentFlyout_OpenSettings"))
                {
                    while (true)
                    {
                        settingsEvent.WaitOne();
                        Application.Current.Dispatcher.Invoke(() => { SettingsWindow.ShowInstance(); });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Settings event listener error");
            }
        });

        try
        {
            SettingsManager.RestoreSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to restore settings: {ex.Message}");
            Logger.Error(ex, "Failed to restore settings");
        }

        // RestoreSettings may replace SettingsManager.Current instance, so rebind DataContext.
        DataContext = SettingsManager.Current;

        if (SettingsManager.Current.Startup == true) // add to startup programs if enabled, needs improvement
        {
            RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            string? executablePath = Environment.ProcessPath;
            if (key != null && executablePath != null)
                key.SetValue("FluentFlyout", executablePath);
        }

        // display tray icon if enabled
        if (!SettingsManager.Current.NIconHide)
        {
            nIcon.Visibility = Visibility.Visible;
        }

        cts = new CancellationTokenSource();

        mediaManager.Start();

        _hookProc = HookCallback;
        _hookId = SetHook(_hookProc);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -Width - 20; // workaround for window appearing on the screen before the animation starts
        CustomWindowChrome.CaptionHeight = 0; // hide the title bar

        mediaManager.OnAnyMediaPropertyChanged += MediaManager_OnAnyMediaPropertyChanged;
        mediaManager.OnAnyPlaybackStateChanged += CurrentSession_OnPlaybackStateChanged;
        mediaManager.OnAnyTimelinePropertyChanged += MediaManager_OnAnyTimelinePropertyChanged;
        mediaManager.OnAnySessionClosed += MediaManager_OnAnySessionClosed;

        WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");
        WM_SHELLHOOK = RegisterWindowMessage("SHELLHOOK");
        RegisterShellHookWindow(new WindowInteropHelper(this).Handle);

        _positionTimer = new Timer(SeekbarUpdateUi, null, Timeout.Infinite, Timeout.Infinite);
        if (_seekBarEnabled && GetActiveMediaSession() is { } session)
        {
            UpdateSeekbarCurrentDuration(session.ControlSession.GetTimelineProperties().Position);
        }

        StartLanguageMonitoring();

        string previousVersion = SettingsManager.Current.LastKnownVersion;
        _ = CheckForExperimentsOnStartupAsync(previousVersion);

        // apply other things on new thread
        Dispatcher.Invoke(() =>
        {
            LocalizationManager.ApplyLocalization();

            try // update last known version. gets the version of the app
            {
                var version = Package.Current.Id.Version;
                SettingsManager.Current.LastKnownVersion = $"{version.Major}.{version.Minor}.{version.Build}-f2.0.0";
            }
            catch
            {
                SettingsManager.Current.LastKnownVersion = UpdateCheckerService.CurrentAppVersion;
            }

            Logger.Info($"Current version: {SettingsManager.Current.LastKnownVersion}");

            Notifications.ShowFirstOrUpdateNotification(previousVersion, SettingsManager.Current.LastKnownVersion);
            FlowDirection = SettingsManager.Current.FlowDirection;

            // check for updates on startup
            _ = CheckForUpdatesOnStartupAsync();
        });
    }

    private async Task CheckForExperimentsOnStartupAsync(string previousVersion)
    {
        await ExperimentsService.GetExperimentsAsync();

        OnboardingExperiment(previousVersion);
    }

    private void OnboardingExperiment(string previousVersion)
    {
        // show onboarding to new users (no previous version stored = user has never run the app before)
        if (string.IsNullOrEmpty(previousVersion))
        {
            if (ExperimentsService.HasExperiments)
            {
                if (ExperimentsService.CheckUuidInExperiment("onboarding") == "A")
                    OnboardingWindow.ShowInstance();
                else
                {
                    SettingsWindow.ShowInstance();
                    _ = TelemetryService.SendTelemetryEventAsync("onboarding_completed", "onboarding");
                }
            }
            else
                OnboardingWindow.ShowInstance();
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result = await UpdateCheckerService.CheckForUpdatesAsync(SettingsManager.Current.LastKnownVersion);

            if (result.Success)
            {
                UpdateState.Current.IsUpdateAvailable = result.IsUpdateAvailable;
                UpdateState.Current.NewestVersion = result.NewestVersion;
                UpdateState.Current.UpdateUrl = result.UpdateUrl;
                UpdateState.Current.LastUpdateCheck = result.CheckedAt;

                if (result.IsUpdateAvailable)
                {
                    Notifications.ShowUpdateAvailableNotification(result.NewestVersion, result.UpdateUrl);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to check for updates on startup");
        }
    }

    public bool IsSessionAllowed(MediaSession? session)
    {
        if (session == null) return false;
        if (!SettingsManager.Current.AppFilteringEnabled) return true;

        string appId = session.Id ?? string.Empty;
        string appName = MediaPlayerData.GetAndCacheMediaPlayerData(appId).Item1 ?? appId;

        if (SettingsManager.Current.AppFilteringMode == 0) // Blacklist mode
        {
            if (SettingsManager.Current.BlockedApps != null && SettingsManager.Current.BlockedApps.Any(b =>
                    appName.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                    appId.Contains(b, StringComparison.OrdinalIgnoreCase)))
                return false;

            return true;
        }
        else // Whitelist mode
        {
            if (SettingsManager.Current.AllowedApps != null && SettingsManager.Current.AllowedApps.Any(a =>
                    appName.Contains(a, StringComparison.OrdinalIgnoreCase) ||
                    appId.Contains(a, StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }
    }

    private string? _manuallySelectedSessionId = null;

    /// <summary>
    /// The currently active session ID (manually selected or auto-detected). No COM calls.
    /// </summary>
    public string? ActiveSessionId => _manuallySelectedSessionId;

    /// <summary>
    /// Indicates whether active media session is currently playing audio.
    /// </summary>
    public static bool IsMediaPlaying { get; internal set; }

    /// <summary>
    /// Returns sessions list with NO playback status checks (no COM calls). Safe to call from UI thread.
    /// </summary>
    public List<MediaSession> GetValidMediaSessions()
    {
        try
        {
            var dict = mediaManager.CurrentMediaSessions;
            if (dict == null) return [];
            return dict.Values.Where(IsSessionAllowed).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Full active session resolution with playback status checks. May do COM calls - call from background.
    /// </summary>
    public MediaSession? GetActiveMediaSession()
    {
        var validSessions = GetValidMediaSessions();
        if (validSessions.Count == 0) return null;

        // 1. Manually selected session takes priority (no COM needed)
        if (!string.IsNullOrEmpty(_manuallySelectedSessionId))
        {
            var selected = validSessions.FirstOrDefault(s => s.Id == _manuallySelectedSessionId);
            if (selected != null)
                return selected;
        }

        // 2. Check Windows GSMTC focused session (no playback COM call)
        try
        {
            var focused = mediaManager.GetFocusedSession();
            if (focused != null && validSessions.Any(s => s.Id == focused.Id))
                return focused;
        }
        catch { }

        // 3. Check for any playing session (COM call - only as last resort)
        var playing = validSessions.FirstOrDefault(s =>
        {
            try { return s.ControlSession?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        });
        if (playing != null)
            return playing;

        return validSessions.FirstOrDefault();
    }

    /// <summary>
    /// Fast cycle - pure index math, zero COM calls, returns instantly.
    /// </summary>
    public void CycleMediaSession(bool next)
    {
        var validSessions = GetValidMediaSessions();
        if (validSessions.Count <= 1) return;

        // Find current index using _manuallySelectedSessionId first (no COM)
        int currentIndex = -1;
        if (!string.IsNullOrEmpty(_manuallySelectedSessionId))
            currentIndex = validSessions.FindIndex(s => s.Id == _manuallySelectedSessionId);

        // Fallback to GSMTC focused (no playback COM call)
        if (currentIndex < 0)
        {
            try
            {
                var focused = mediaManager.GetFocusedSession();
                if (focused != null)
                    currentIndex = validSessions.FindIndex(s => s.Id == focused.Id);
            }
            catch { }
        }

        if (currentIndex < 0) currentIndex = 0;

        int newIndex = next
            ? (currentIndex + 1) % validSessions.Count
            : (currentIndex - 1 + validSessions.Count) % validSessions.Count;

        _manuallySelectedSessionId = validSessions[newIndex].Id;
        UpdateTaskbar(); // async, non-blocking
    }

    public void SelectMediaSession(MediaSession session)
    {
        if (session == null || !IsSessionAllowed(session))
            return;

        _manuallySelectedSessionId = session.Id;
        UpdateTaskbar(); // async, non-blocking
    }

    public bool IsWidgetSessionAllowed(MediaSession? session)
    {
        if (session == null) return false;
        return IsSessionAllowed(session);
    }

    public MediaSession? GetActiveWidgetMediaSession()
    {
        return GetActiveMediaSession();
    }

    public void RefreshFilteredMedia()
    {
        UpdateTaskbar();

        if (IsVisible)
        {
            var activeSession = GetActiveMediaSession();

            // UpdateUI handles a null value internally so we haven't checked for null here.
            UpdateUI(activeSession!);

            if (activeSession != null)
            {
                HandlePlayBackState(activeSession.ControlSession.GetPlaybackInfo()?.PlaybackStatus);
            }
            else
            {
                HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
                ShowMediaFlyout(toggleMode: true);
            }
        }
    }

    private static GlobalSystemMediaTransportControlsSessionMediaProperties? TryGetMediaProperties(GlobalSystemMediaTransportControlsSession controlSession)
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

    private void openSettings(object? sender, EventArgs e)
    {
        SettingsWindow.ShowInstance();
    }

    public static int getDuration() // get the duration of the animation based on the speed setting
    {
        int msDuration = SettingsManager.Current.FlyoutAnimationSpeed switch
        {
            0 => 0, // off
            1 => 150, // 0.5x
            2 => 300, // 1x
            3 => 450, // 1.5x
            4 => 600, // 2x
            _ => 900 // 3x
        };
        return msDuration;
    }

    public EasingFunctionBase getEasingStyle(bool easeOut)
    {
        EasingMode easingMode = easeOut ? EasingMode.EaseOut : EasingMode.EaseIn;
        EasingFunctionBase easingStyle = SettingsManager.Current.FlyoutAnimationEasingStyle switch
        {
            // 0 is linear, null
            1 => new SineEase { EasingMode = easingMode }, // sine
            2 => new QuadraticEase { EasingMode = easingMode }, // quadratic
            _ => new CubicEase { EasingMode = easingMode }, // cubic
        };
        return easingStyle;
    }

    private MonitorUtil.MonitorInfo getSelectedMonitor()
    {
        return MonitorUtil.GetSelectedMonitor(SettingsManager.Current.FlyoutSelectedMonitor);
    }

    /// <summary>
    /// Computes the final resting position (left, top) for a window based on the current
    /// position setting and the selected monitor's work area.
    /// </summary>
    private (double left, double top) GetFinalPosition(Rect windowRect, Rect workArea)
    {
        int position = SettingsManager.Current.Position;
        double left = position switch
        {
            0 or 3 => workArea.Left + 16,
            2 or 5 => workArea.Left + workArea.Width - windowRect.Width - 16,
            _ => workArea.Left + workArea.Width / 2 - windowRect.Width / 2
        };
        double top = position switch
        {
            0 or 2 => workArea.Top + workArea.Height - windowRect.Height - 16,
            1 => workArea.Top + workArea.Height - windowRect.Height - 80,
            _ => workArea.Top + 16
        };
        return (left, top);
    }

    public void OpenAnimation(MicaWindow window, bool alwaysBottom = false, MonitorInfo? selectedMonitor = null, MicaWindow? aboveReference = null, Rect? anchorRect = null)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers.Actions[0] as BeginStoryboard;
        var storyboard = beginStoryboard.Storyboard;

        DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];
        var monitor = selectedMonitor != null ? selectedMonitor.Value : getSelectedMonitor();
        if (anchorRect is { Width: > 0, Height: > 0 } monitorAnchor)
        {
            Point anchorCenter = new(
                monitorAnchor.Left + monitorAnchor.Width / 2,
                monitorAnchor.Top + monitorAnchor.Height / 2);
            MonitorInfo anchorMonitor = MonitorUtil.GetMonitors()
                .FirstOrDefault(item => item.monitorArea.Contains(anchorCenter));
            if (anchorMonitor.monitorArea.Width > 0 && anchorMonitor.monitorArea.Height > 0)
                monitor = anchorMonitor;
        }
        var workArea = monitor.workArea;

        // prevent flickering
        WindowHelper.SetVisibility(window, false); // window.Visibility = Visibility.Hidden works with some delay

        // Update the DPI by moving the window to the target workArea, ignoring WPF scaling
        WindowHelper.SetPosition(window, workArea.Left, workArea.Top);
        var windowRect = WindowHelper.GetPlacement(window); // here we take the updated window size in raw coordinates.

        double window_left = 0;

        // Use the taskbar element's actual screen rectangle when available.
        // This stays correct when neighbouring taskbar blocks move the widget.
        if (anchorRect is { Width: > 0, Height: > 0 } anchor)
        {
            const double gap = 8;
            const double edgePadding = 8;

            window_left = anchor.Left + anchor.Width / 2 - windowRect.Width / 2;
            window_left = Math.Clamp(
                window_left,
                workArea.Left + edgePadding,
                Math.Max(workArea.Left + edgePadding, workArea.Right - windowRect.Width - edgePadding));

            bool placeAbove = anchor.Top > workArea.Top + workArea.Height / 2;
            double targetTop = placeAbove
                ? anchor.Top - windowRect.Height - gap
                : anchor.Bottom + gap;
            targetTop = Math.Clamp(
                targetTop,
                workArea.Top + edgePadding,
                Math.Max(workArea.Top + edgePadding, workArea.Bottom - windowRect.Height - edgePadding));

            moveAnimation.To = targetTop;
            if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                moveAnimation.From = targetTop;
            else
                moveAnimation.From = targetTop + (placeAbove ? 20 : -20);
        }
        // If a reference window is provided and visible, position the window next to it
        else if (aboveReference != null && aboveReference.IsVisible)
        {
            // Here we work with raw monitor coordinates, without taking DPI into account.
            double refWidth = aboveReference.Width * monitor.dpiX / 96.0;
            double refHeight = aboveReference.Height * monitor.dpiY / 96.0;
            var refRect = new Rect(0, 0, refWidth, refHeight);
            var (refLeft, refTop) = GetFinalPosition(refRect, workArea);

            window_left = refLeft + refWidth / 2 - windowRect.Width / 2;
            double aboveTop = refTop - windowRect.Height - 8;
            bool isTop = SettingsManager.Current.Position switch
            {
                3 or 4 or 5 => true,
                _ => false
            };

            // If the reference window is too close to the top edge, we place the flyout below it instead of above to prevent it from going off-screen.
            if (isTop)
                aboveTop = refTop + refHeight + 8;

            moveAnimation.To = aboveTop;
            if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                moveAnimation.From = moveAnimation.To;
            else
                moveAnimation.From = isTop ? aboveTop - 20 : aboveTop + 20;
        }
        // default behavior: position the flyout based on the user's settings
        else if (alwaysBottom == false)
        {
            _position = SettingsManager.Current.Position;
            if (_position == 0)
            {
                window_left = workArea.Left + 16;
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0) // if off, don't animate (just appear at the bottom)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height + 4; // appear from the bottom of the screen
            }
            else if (_position == 1)
            {
                window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 80;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height - 60;
            }
            else if (_position == 2)
            {
                window_left = workArea.Left + workArea.Width - windowRect.Width - 16;
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height + 4;
            }
            else if (_position == 3)
            {
                window_left = workArea.Left + 16;
                moveAnimation.To = workArea.Top + 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + -4;
            }
            else if (_position == 4)
            {
                window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
                moveAnimation.To = workArea.Top + 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + -4;
            }
            else if (_position == 5)
            {
                window_left = workArea.Left + workArea.Width - windowRect.Width - 16;
                moveAnimation.To = workArea.Top + 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + -4;
            }
        }
        // other cases (e.g. if alwaysBottom is true): position the flyout at the bottom center of the screen
        else
        {
            window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
            moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16;
            if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                moveAnimation.From = moveAnimation.To;
            else
                moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height + 4;
        }

        // Set the initial position in raw coordinates.
        WindowHelper.SetPosition(window, window_left, moveAnimation.From!.Value);

        // Next coordinates will be used to set Window.Top, which takes DPI into account,
        // so we need to convert the coordinates to DPI scale.
        moveAnimation.From *= 96.0 / monitor.dpiY;
        moveAnimation.To *= 96.0 / monitor.dpiY;

        int msDuration = getDuration();

        DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        if (SettingsManager.Current.FlyoutAnimationSpeed != 0) opacityAnimation.From = 0;
        opacityAnimation.To = 1;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        if (SettingsManager.Current.FlyoutAnimationEasingStyle == 0) moveAnimation.EasingFunction = opacityAnimation.EasingFunction = null;
        else moveAnimation.EasingFunction = opacityAnimation.EasingFunction = getEasingStyle(true);
        moveAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        storyboard.Begin(window);
        WindowHelper.SetVisibility(window, true);
        WindowHelper.SetTopmost(window);
    }

    public void CloseAnimation(MicaWindow window, MonitorInfo? selectedMonitor = null)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers.Actions[0] as BeginStoryboard;
        var storyboard = beginStoryboard.Storyboard;

        DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];
        var monitor = selectedMonitor != null ? selectedMonitor.Value : getSelectedMonitor();
        var workArea = monitor.workArea;
        var windowRect = WindowHelper.GetPlacement(window);

        // Use the window's actual current position as the animation start
        moveAnimation.From = windowRect.Top;

        if (SettingsManager.Current.FlyoutAnimationSpeed != 0)
        {
            // Determine slide direction
            bool isTopHalf = windowRect.Top + windowRect.Height / 2 < workArea.Top + workArea.Height / 2;
            moveAnimation.To = windowRect.Top + (isTopHalf ? -20 : 20);
        }

        moveAnimation.From *= 96.0 / monitor.dpiY;
        if (moveAnimation.To != null)
            moveAnimation.To *= 96.0 / monitor.dpiY;

        int msDuration = getDuration();

        DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        opacityAnimation.From = 1;
        if (SettingsManager.Current.FlyoutAnimationSpeed != 0) opacityAnimation.To = 0;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        if (SettingsManager.Current.FlyoutAnimationEasingStyle == 0) moveAnimation.EasingFunction = opacityAnimation.EasingFunction = null;
        else moveAnimation.EasingFunction = opacityAnimation.EasingFunction = getEasingStyle(false);
        moveAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        storyboard.Begin(window);
    }

    // DispatcherTimer debounce - UpdateTaskbar() just resets the timer.
    // The actual work fires once on UI thread after 60ms of silence.
    // This eliminates race conditions from concurrent background-thread calls.
    private System.Windows.Threading.DispatcherTimer? _updateTaskbarTimer;
    private const int UpdateTaskbarDebounceMs = 60;

    // Guard against concurrent UpdateTaskbarNow executions (can happen when
    // the timer fires while a previous async update is still awaiting COM result)
    private bool _isUpdatingTaskbar = false;
    private bool _pendingTaskbarUpdate = false;

    public void UpdateTaskbar()
    {
        // If called from a background thread, marshal to UI thread first
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateTaskbar, System.Windows.Threading.DispatcherPriority.Normal);
            return;
        }

        // Reset the debounce timer - only fires after UpdateTaskbarDebounceMs of no more calls
        if (_updateTaskbarTimer == null)
        {
            _updateTaskbarTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(UpdateTaskbarDebounceMs)
            };
            _updateTaskbarTimer.Tick += UpdateTaskbarNow;
        }

        _updateTaskbarTimer.Stop();
        _updateTaskbarTimer.Start();
    }

    private async void UpdateTaskbarNow(object? sender, EventArgs e)
    {
        _updateTaskbarTimer?.Stop();

        // If already in the middle of an async COM request, mark a pending update
        // and return - the finally block below will trigger one more cycle.
        if (_isUpdatingTaskbar)
        {
            _pendingTaskbarUpdate = true;
            return;
        }

        _isUpdatingTaskbar = true;
        _pendingTaskbarUpdate = false;

        try
        {
            // Fast session lookup - no COM, pure in-memory dict lookup
            var validSessions = GetValidMediaSessions();
            MediaSession? activeSession = null;
            if (!string.IsNullOrEmpty(_manuallySelectedSessionId))
                activeSession = validSessions.FirstOrDefault(s => s.Id == _manuallySelectedSessionId);
            activeSession ??= validSessions.FirstOrDefault();

            if (!mediaManager.IsStarted || activeSession?.ControlSession == null)
            {
                IsMediaPlaying = false;
                taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
                return;
            }

            var controlSession = activeSession.ControlSession;

            // Move ALL COM calls to thread pool - UI thread stays fully responsive
            var (songInfo, playbackInfo) = await Task.Run(async () =>
            {
                var props = await controlSession.TryGetMediaPropertiesAsync().AsTask();
                var pbInfo = controlSession.GetPlaybackInfo();
                return (props, pbInfo);
            });

            IsMediaPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;


            // BitmapHelper creates WPF objects - must run back on UI thread (already here after await)
            var thumbnail = songInfo != null ? BitmapHelper.GetThumbnail(songInfo.Thumbnail) : null;
            if (thumbnail != null)
            {
                BitmapHelper.GetDominantColors(1);
            }

            taskbarWindow?.UpdateUi(
                songInfo?.Title ?? "-",
                songInfo?.Artist ?? "-",
                thumbnail,
                playbackInfo?.PlaybackStatus,
                playbackInfo?.Controls);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Error updating taskbar media properties");
        }
        finally
        {
            _isUpdatingTaskbar = false;

            // If a new update was requested while we were busy, run one more
            if (_pendingTaskbarUpdate)
            {
                _pendingTaskbarUpdate = false;
                UpdateTaskbar();
            }
        }
    }


    public void reportBug(object? sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/KoksMen/FluentFlyout/issues/new/choose",
            UseShellExecute = true
        });
    }

    private void openRepository(object? sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/KoksMen/FluentFlyout",
            UseShellExecute = true
        });
    }

    public void openLogsFolder(object? sender, EventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", FileSystemHelper.GetLogsPath());
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open logs folder");
        }
    }

    private void pauseOtherMediaSessionsIfNeeded(MediaSession mediaSession)
    {
        if (
            SettingsManager.Current.PauseOtherSessionsEnabled
            && mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            )
        {
            PauseOtherSessions(mediaSession);
        }
    }

    private void CurrentSession_OnPlaybackStateChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null)
    {
        // This is called from a background thread - do the minimum here, then marshal to UI thread
        pauseOtherMediaSessionsIfNeeded(mediaSession);

        if (IsSessionAllowed(mediaSession) && mediaSession.ControlSession != null)
        {
            try
            {
                var status = playbackInfo?.PlaybackStatus ?? mediaSession.ControlSession.GetPlaybackInfo()?.PlaybackStatus;
                IsMediaPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    // Auto-switch to the session that just started playing
                    _manuallySelectedSessionId = mediaSession.Id;
                }
            }
            catch { }
        }

        // Marshal all UI work to UI thread (non-blocking BeginInvoke)
        Dispatcher.BeginInvoke(() =>
        {
            UpdateTaskbar();

            if (IsVisible)
            {
                var focusedSession = GetActiveMediaSession();
                if (focusedSession != null)
                {
                    UpdateUI(focusedSession);
                    HandlePlayBackState(playbackInfo?.PlaybackStatus);
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Normal);
    }

    // for determining whether MediaPropertyChanged has no changes
    private string previousMediaProperty = "";
    private int previousMediaPropertyThumbnail = 0;
    private void MediaManager_OnAnyMediaPropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
    {
        // This is called from a background thread - do the minimum here, then marshal to UI thread
        if (mediaSession.ControlSession == null)
            return;

        // Marshal all UI work to UI thread (non-blocking BeginInvoke)
        Dispatcher.BeginInvoke(() =>
        {
            UpdateTaskbar();

            if (IsVisible)
            {
                var currentActiveSession = GetActiveMediaSession();
                if (currentActiveSession != null)
                    UpdateUI(currentActiveSession);
            }
        }, System.Windows.Threading.DispatcherPriority.Normal);

        pauseOtherMediaSessionsIfNeeded(mediaSession);

        var playbackInfo = mediaSession.ControlSession.GetPlaybackInfo();
        var thumbnail = BitmapHelper.GetThumbnail(mediaProperties.Thumbnail);

        // Feature 2: per-flyout fullscreen setting for Next Up
        if (SettingsManager.Current.NextUpEnabled && !FullscreenDetector.ShouldSuppressForFullscreen(SettingsManager.Current.NextUpFlyoutShowInExclusiveFullscreen, SettingsManager.Current.NextUpFlyoutShowInBorderless)) // show NextUpWindow if enabled in settings
        {
            void createNewNextUpWindow()
            {
                Dispatcher.Invoke(() =>
                {
                    if (nextUpWindow == null && playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) // double-check within the Dispatcher to prevent race conditions
                    {
                        nextUpWindow = new NextUpWindow(mediaProperties.Title, mediaProperties.Artist, thumbnail);
                        currentTitle = mediaProperties.Title;
                        nextUpWindow.Closed += (s, e) => nextUpWindow = null; // set nextUpWindow to null when closed
                    }
                });
            }

            if (nextUpWindow == null && IsVisible == false && mediaProperties.Thumbnail != null && currentTitle != mediaProperties.Title)
            {
                createNewNextUpWindow();
            }
            else if (nextUpWindow != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (nextUpWindow != null)
                    {
                        WindowHelper.SetVisibility(nextUpWindow, false); // prevents rare flickering during rapid closing
                        nextUpWindow.Close(); // must be cleared by the Closed event
                    }
                });
                createNewNextUpWindow();
            }
            else if (nextUpWindow != null && mediaProperties.Thumbnail != null)
            {
                Dispatcher.Invoke(() =>
                {
                    nextUpWindow?.UpdateThumbnail(thumbnail);
                });
            }
        }

        if (IsVisible)
        {
            var focusedSession = GetActiveMediaSession();
            if (focusedSession != null)
            {
                HandlePlayBackState(focusedSession.ControlSession.GetPlaybackInfo()?.PlaybackStatus);
                UpdateUI(focusedSession);
            }
        }
    }

    private void MediaManager_OnAnyTimelinePropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        if (GetActiveMediaSession() is not { } session || session.Id != mediaSession.Id) return;

        if (_seekBarEnabled)
        {
            Dispatcher.Invoke(() =>
            {
                if (Visibility != Visibility.Visible || _isHiding || _isDragging) return;

                _lastSelfUpdateTimestamp = DateTime.Now;
                UpdateSeekbarCurrentDuration(session.ControlSession.GetTimelineProperties().Position);
                HandlePlayBackState(session.ControlSession.GetPlaybackInfo().PlaybackStatus);
            });
        }
    }

    private void MediaManager_OnAnySessionClosed(MediaSession mediaSession)
    {
#if DEBUG
        Logger.Debug("Session closed: " + (mediaSession.Id).ToString());
#endif
        UpdateTaskbar();
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc) // set the keyboard hook
    {
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule? curModule = curProcess.MainModule;
        if (curModule == null)
        {
            Logger.Warn("Failed to set keyboard hook - FluentFlyout will now rely on WndProc only");
            return IntPtr.Zero;
        }
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_KEYUP))
        {
            int vkCode = Marshal.ReadInt32(lParam);

            bool mediaKeysPressed = vkCode == 0xB3 || vkCode == 0xB0 || vkCode == 0xB1 || vkCode == 0xB2; // Play/Pause, next, previous, stop
            bool volumeKeysPressed = vkCode == 0xAD || vkCode == 0xAE || vkCode == 0xAF; // Mute, Volume Down, Volume Up

            // Feature 3: record when the Play/Pause key is pressed so ShowMediaFlyout can suppress on pause
            if (vkCode == 0xB3 && wParam == WM_KEYDOWN) // VK_MEDIA_PLAY_PAUSE
            {
                _pauseKeyPressed = true;
                // Auto-reset after 1 s to avoid stale flag in edge cases
                _pauseKeyResetTimer?.Dispose();
                _pauseKeyResetTimer = new Timer(_ => _pauseKeyPressed = false, null, 1000, Timeout.Infinite);
            }

            // MainWindow.WndProc() also handles media and volume keys
            if (mediaKeysPressed || volumeKeysPressed)
            {
                // Feature 1: global master switch
                if (!SettingsManager.Current.GlobalFlyoutEnabled)
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                bool result = false;
                if (mediaKeysPressed || (!SettingsManager.Current.MediaFlyoutVolumeKeysExcluded && volumeKeysPressed))
                    result = TryShowMediaFlyoutDebounced();

                if (SettingsManager.Current.VolumeControlEnabled && volumeKeysPressed)
                {
                    volumeMixerWindow?.ViewModel.SyncMasterFromDevice();
                    volumeMixerWindow?.ShowFlyout();
                }

                if (!result)
                {
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }
            }

            // Feature 1: global master switch for lock keys
            if (SettingsManager.Current.GlobalFlyoutEnabled
                && SettingsManager.Current.LockKeysEnabled
                // Feature 2: per-flyout fullscreen setting for lock keys
                && !FullscreenDetector.ShouldSuppressForFullscreen(SettingsManager.Current.LockKeysFlyoutShowInExclusiveFullscreen, SettingsManager.Current.LockKeysFlyoutShowInBorderless)
                && wParam == WM_KEYUP)
            {
                if (vkCode == 0x14 && SettingsManager.Current.LockKeysCapsEnabled) // Caps Lock
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout(FindResource("LockWindow_CapsLock").ToString(), Keyboard.IsKeyToggled(Key.CapsLock));
                }
                else if (vkCode == 0x90 && SettingsManager.Current.LockKeysNumEnabled) // Num Lock
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout(FindResource("LockWindow_NumLock").ToString(), Keyboard.IsKeyToggled(Key.NumLock));
                }
                else if (vkCode == 0x91 && SettingsManager.Current.LockKeysScrollEnabled) // Scroll Lock
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout(FindResource("LockWindow_ScrollLock").ToString(), Keyboard.IsKeyToggled(Key.Scroll));
                }
                else if (vkCode == 0x2D && SettingsManager.Current.LockKeysInsertEnabled) // Insert
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout("Insert", Keyboard.IsKeyToggled(Key.Insert));
                }

                // Also check language layout change on modifier key releases (Alt, Shift, Ctrl, Win, Space, Caps)
                if (vkCode == 0x12 || vkCode == 0x10 || vkCode == 0x11 || vkCode == 0x5B || vkCode == 0x5C || vkCode == 0x20 || vkCode == 0x14)
                {
                    CheckKeyboardLayoutChange(triggeredByKey: true);
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // show the media flyout with debounce
    private bool TryShowMediaFlyoutDebounced()
    {
        long currentTime = Environment.TickCount64;
        // debounce to prevent hangs with rapid key presses
        if ((currentTime - _lastFlyoutTime) < 500) // 500ms debounce time
        {
            return false;
        }
        _lastFlyoutTime = currentTime;
        ShowMediaFlyout();
        return true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private IntPtr _lastKeyboardLayout = IntPtr.Zero;
    private IntPtr _lastForegroundHwnd = IntPtr.Zero;
    private DispatcherTimer? _langCheckTimer;

    private void StartLanguageMonitoring()
    {
        _langCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _langCheckTimer.Tick += (s, e) => CheckKeyboardLayoutChange(triggeredByKey: false);
        _langCheckTimer.Start();
    }

    public void CheckKeyboardLayoutChange(bool triggeredByKey = false)
    {
        if (!SettingsManager.Current.GlobalFlyoutEnabled) return;
        if (!SettingsManager.Current.LockKeysEnabled) return;
        if (!SettingsManager.Current.LockKeysLanguageFlyoutEnabled) return;
        if (FullscreenDetector.ShouldSuppressForFullscreen(
            SettingsManager.Current.LockKeysFlyoutShowInExclusiveFullscreen,
            SettingsManager.Current.LockKeysFlyoutShowInBorderless)) return;

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        uint threadId = GetWindowThreadProcessId(hwnd, out _);
        IntPtr currentHkl = GetKeyboardLayout(threadId);
        if (currentHkl == IntPtr.Zero) return;

        if (_lastKeyboardLayout == IntPtr.Zero)
        {
            _lastKeyboardLayout = currentHkl;
            _lastForegroundHwnd = hwnd;
            return;
        }

        if (currentHkl != _lastKeyboardLayout)
        {
            bool isSameWindow = (hwnd == _lastForegroundHwnd);
            _lastKeyboardLayout = currentHkl;
            _lastForegroundHwnd = hwnd;

            if (isSameWindow || triggeredByKey)
            {
                string langText = GetLanguageDisplayName(currentHkl);
                Dispatcher.Invoke(() =>
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLanguageFlyout(langText);
                });
            }
        }
        else
        {
            _lastForegroundHwnd = hwnd;
        }
    }

    private string GetLanguageDisplayName(IntPtr hkl)
    {
        ushort langId = (ushort)((long)hkl & 0xFFFF);
        try
        {
            var culture = new System.Globalization.CultureInfo(langId);
            string shortName = culture.TwoLetterISOLanguageName.ToUpper();

            string mainName = culture.Parent != null && !string.IsNullOrEmpty(culture.Parent.NativeName)
                ? culture.Parent.NativeName
                : culture.NativeName;

            if (!string.IsNullOrEmpty(mainName))
            {
                mainName = char.ToUpper(mainName[0]) + mainName.Substring(1);
            }

            return $"{mainName} ({shortName})";
        }
        catch
        {
            return "Language Changed";
        }
    }

    private DateTime _lastMediaFlyoutCloseTime = DateTime.MinValue;

    private async void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (Visibility == Visibility.Visible && !_isHiding)
        {
            _lastMediaFlyoutCloseTime = DateTime.UtcNow;
            CloseAnimation(this);
            _isHiding = true;
            cts.Cancel();
            await Task.Delay(getDuration());
            if (_isHiding)
            {
                Hide();
                if (_seekBarEnabled)
                    HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
            }
        }
    }

    public async void ShowMediaFlyout(bool toggleMode = false, bool forceShow = false, bool anchorToTaskbarWidget = true)
    {
        if (toggleMode && (DateTime.UtcNow - _lastMediaFlyoutCloseTime).TotalMilliseconds < 400)
        {
            return;
        }

        // If in toggle mode and flyout is visible, close it unconditionally
        if (toggleMode && Visibility == Visibility.Visible && !_isHiding)
        {
            _lastMediaFlyoutCloseTime = DateTime.UtcNow;
            CloseAnimation(this);
            _isHiding = true;
            cts.Cancel();
            await Task.Delay(getDuration());
            if (_isHiding)
            {
                Hide();
                if (_seekBarEnabled)
                    HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
            }
            return;
        }

        var activeSession = GetActiveMediaSession();

        // Feature 1: global master switch
        if (!forceShow && !SettingsManager.Current.GlobalFlyoutEnabled)
            return;

        // Feature 3: suppress flyout when pausing via keyboard
        if (!forceShow
            && SettingsManager.Current.HideAudioFlyoutOnPause
            && _pauseKeyPressed)
        {
            // Check if the resulting state will be paused
            var playbackStatus = activeSession?.ControlSession?.GetPlaybackInfo()?.PlaybackStatus;
            if (playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                // Currently playing → pressing play/pause will pause it → suppress
                _pauseKeyPressed = false;
                return;
            }
        }

        if (activeSession == null ||
            (!forceShow && !SettingsManager.Current.MediaFlyoutEnabled) ||
            // Feature 2: per-flyout fullscreen setting for media flyout
            FullscreenDetector.ShouldSuppressForFullscreen(SettingsManager.Current.MediaFlyoutShowInExclusiveFullscreen, SettingsManager.Current.MediaFlyoutShowInBorderless))
            return;

        UpdateUI(activeSession);
        if (_seekBarEnabled)
            HandlePlayBackState(activeSession.ControlSession.GetPlaybackInfo().PlaybackStatus);

        if (nextUpWindow != null) // close NextUpWindow if it's open
        {
            nextUpWindow.Close();
            nextUpWindow = null;
        }

        Rect? widgetAnchor = null;
        if (anchorToTaskbarWidget
            && taskbarWindow?.TryGetMediaWidgetScreenRect(out Rect actualWidgetRect) == true)
        {
            widgetAnchor = actualWidgetRect;
        }

        if (_isHiding == true)
        {
            _isHiding = false;
            OpenAnimation(this, anchorRect: widgetAnchor);
        }
        cts.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        Visibility = Visibility.Visible;
        WindowHelper.SetTopmost(this);
        Activate();

        DateTime flyoutOpenTime = DateTime.UtcNow;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(30, token);

                bool mouseOverMedia = WindowHelper.IsMouseOverWindow(this);
                bool mouseOverVolume = SettingsManager.Current.VolumeControlAboveMediaFlyout
                    && SettingsManager.Current.VolumeControlEnabled
                    && volumeMixerWindow != null
                    && volumeMixerWindow.IsVisible
                    && WindowHelper.IsMouseOverWindow(volumeMixerWindow);

                bool mouseOverWidget = taskbarWindow != null
                    && taskbarWindow.IsVisible
                    && taskbarWindow.TryGetMediaWidgetScreenRect(out Rect widgetRect)
                    && widgetRect.Contains(System.Windows.Forms.Control.MousePosition.X, System.Windows.Forms.Control.MousePosition.Y);

                if ((DateTime.UtcNow - flyoutOpenTime).TotalMilliseconds > 300)
                {
                    bool isLClick = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                    bool isRClick = (GetAsyncKeyState(0x02) & 0x8000) != 0;
                    if ((isLClick || isRClick) && !mouseOverMedia && !mouseOverVolume && !mouseOverWidget)
                    {
                        _lastMediaFlyoutCloseTime = DateTime.UtcNow;
                        CloseAnimation(this);
                        _isHiding = true;
                        await Task.Delay(getDuration());
                        if (_isHiding == false) return;
                        Hide();
                        if (_seekBarEnabled)
                            HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
                        break;
                    }
                }

                if (!mouseOverMedia && !mouseOverVolume && !SettingsManager.Current.MediaFlyoutAlwaysDisplay)
                {
                    await Task.Delay(SettingsManager.Current.Duration, token);

                    mouseOverMedia = WindowHelper.IsMouseOverWindow(this);
                    mouseOverVolume = SettingsManager.Current.VolumeControlAboveMediaFlyout
                        && SettingsManager.Current.VolumeControlEnabled
                        && volumeMixerWindow != null
                        && volumeMixerWindow.IsVisible
                        && WindowHelper.IsMouseOverWindow(volumeMixerWindow);

                    if (!mouseOverMedia && !mouseOverVolume)
                    {
                        CloseAnimation(this);
                        _isHiding = true;
                        await Task.Delay(getDuration());
                        if (_isHiding == false) return;
                        Hide();
                        if (_seekBarEnabled)
                            HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
                        break;
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            // task was canceled, do nothing
        }
    }

    private void UpdateMediaFlyoutCloseButtonVisibility()
    {
        bool closeVisible = SettingsManager.Current.MediaFlyoutAlwaysDisplay && !SettingsManager.Current.CompactLayout;
        MediaFlyoutCloseButton.Visibility = closeVisible ? Visibility.Visible : Visibility.Collapsed;
        ControlClose.Visibility = SettingsManager.Current.MediaFlyoutAlwaysDisplay && SettingsManager.Current.CompactLayout ? Visibility.Visible : Visibility.Collapsed;
        PlayerSwitcherPanel.Margin = closeVisible ? new Thickness(0, 0, 24, 2) : new Thickness(0, 0, 0, 2);
    }

    private void UpdateUI(MediaSession mediaSession)
    {
        if (_layout != SettingsManager.Current.CompactLayout ||
            _shuffleEnabled != SettingsManager.Current.ShuffleEnabled ||
            _repeatEnabled != SettingsManager.Current.RepeatEnabled ||
            _playerInfoEnabled != SettingsManager.Current.PlayerInfoEnabled ||
            _centerTitleArtist != SettingsManager.Current.CenterTitleArtist ||
            _seekBarEnabled != SettingsManager.Current.SeekbarEnabled ||
            _alwaysDisplay != SettingsManager.Current.MediaFlyoutAlwaysDisplay)
            UpdateUILayout();

        // sometimes mediaSession.ControlSession can be null
        if (mediaSession.ControlSession == null)
            return;

        var controlSession = mediaSession.ControlSession;

        Dispatcher.Invoke(() =>
        {
            UpdateMediaFlyoutCloseButtonVisibility();
            this.EnableBackdrop(); // ensures the backdrop is enabled as sometimes it gets disabled

            if (mediaSession == null)
            {
                SongTitle.Text = "No media playing";
                SongArtist.Text = string.Empty;
                SongImage.ImageSource = null;
                SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Stop16;
                ControlPlayPause.IsEnabled = false;
                ControlPlayPause.Opacity = 0.35;
                ControlBack.IsEnabled = ControlForward.IsEnabled = false;
                ControlBack.Opacity = ControlForward.Opacity = 0.35;
                SongInfoStackPanel.ToolTip = string.Empty;
                return;
            }

            var mediaProperties = controlSession.GetPlaybackInfo();
            if (mediaProperties != null)
            {
                if (mediaProperties.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause16;
                }
                else
                {
                    SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Play16;
                }

                ControlPlayPause.IsEnabled = mediaProperties.Controls.IsPlayEnabled || mediaProperties.Controls.IsPauseEnabled;

                if (ControlPlayPause.IsEnabled)
                {
                    ControlPlayPause.Opacity = 1;
                }
                else
                {
                    ControlPlayPause.Opacity = 0.35;
                }

                ControlBack.IsEnabled = ControlForward.IsEnabled = mediaProperties.Controls.IsNextEnabled;
                ControlBack.Opacity = ControlForward.Opacity = mediaProperties.Controls.IsNextEnabled ? 1 : 0.35;

                if (SettingsManager.Current.RepeatEnabled && !SettingsManager.Current.CompactLayout)
                {
                    ControlRepeat.Visibility = Visibility.Visible;
                    ControlRepeat.IsEnabled = mediaProperties.Controls.IsRepeatEnabled;
                    ControlRepeat.Opacity = mediaProperties.Controls.IsRepeatEnabled ? 1 : 0.35;
                    if (mediaProperties.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.List)
                    {
                        SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAll24;
                        SymbolRepeat.Opacity = 1;
                    }
                    else if (mediaProperties.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.Track)
                    {
                        SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeat124;
                        SymbolRepeat.Opacity = 1;
                    }
                    else if (mediaProperties.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.None)
                    {
                        SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAllOff24;
                        SymbolRepeat.Opacity = 0.5;
                    }
                }
                else ControlRepeat.Visibility = Visibility.Collapsed;


                if (SettingsManager.Current.ShuffleEnabled && !SettingsManager.Current.CompactLayout)
                {
                    ControlShuffle.Visibility = Visibility.Visible;
                    ControlShuffle.IsEnabled = mediaProperties.Controls.IsShuffleEnabled;
                    ControlShuffle.Opacity = mediaProperties.Controls.IsShuffleEnabled ? 1 : 0.35;
                    if (mediaProperties.IsShuffleActive == true)
                    {
                        SymbolShuffle.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24;
                        SymbolShuffle.Opacity = 1;
                    }
                    else
                    {
                        SymbolShuffle.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24;
                        SymbolShuffle.Opacity = 0.5;
                    }
                }
                else ControlShuffle.Visibility = Visibility.Collapsed;


                if (SettingsManager.Current.PlayerInfoEnabled && !SettingsManager.Current.CompactLayout)
                {
                    MediaIdStackPanel.Visibility = Visibility.Visible;
                    (string title, ImageSource? Icon) = MediaPlayerData.GetAndCacheMediaPlayerData(mediaSession.Id);
                    MediaId.Text = title;
                    if (Icon != null)
                    {
                        MediaIdIcon.Source = Icon;
                        MediaIdIcon.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        MediaIdIcon.Visibility = Visibility.Collapsed;
                    }
                }
                else MediaIdStackPanel.Visibility = Visibility.Collapsed;

                // background blurred image visibility setting
                BackgroundImageStyle1.Visibility = SettingsManager.Current.MediaFlyoutBackgroundBlur == 1 ? Visibility.Visible : Visibility.Collapsed;
                BackgroundImageStyle2.Visibility = SettingsManager.Current.MediaFlyoutBackgroundBlur == 2 ? Visibility.Visible : Visibility.Collapsed;
                BackgroundImageStyle3.Visibility = SettingsManager.Current.MediaFlyoutBackgroundBlur == 3 ? Visibility.Visible : Visibility.Collapsed;

                // color play/pause button
                if (BitmapHelper.SavedDominantColors.Count > 0)
                {
                    SolidColorBrush brush = BitmapHelper.SavedDominantColors.First();
                    ControlPlayPause.Background = brush;
                }

                // acrylic effect setting
                if (SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled != _acrylicEnabled
                || SettingsManager.Current.AppTheme != _themeOption) // if theme changes, reapply acrylic for updated background color
                {
                    _acrylicEnabled = SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled;
                    ToggleBlur(); // called enabled but it actually toggles based on the setting
                }
            }

            var songInfo = TryGetMediaProperties(controlSession);
            if (songInfo == null)
                return;

            if (songInfo != null)
            {
                SongTitle.Text = songInfo.Title;
                SongArtist.Text = songInfo.Artist;
                var image = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
                SongImage.ImageSource = image;
                if (image != null)
                {
                    BitmapHelper.GetDominantColors(1);
                }

                // set tooltip
                SongInfoStackPanel.ToolTip = string.Empty;
                SongInfoStackPanel.ToolTip += !String.IsNullOrEmpty(songInfo.Title) ? songInfo.Title : string.Empty;
                SongInfoStackPanel.ToolTip += !String.IsNullOrEmpty(songInfo.Artist) ? "\n\n" + songInfo.Artist : string.Empty;

                // Player switcher panel - show only when multiple sessions exist
                var allSessions = GetValidMediaSessions();
                if (allSessions.Count > 1)
                {
                    PlayerSwitcherPanel.Visibility = Visibility.Visible;
                    string appId = mediaSession.Id ?? string.Empty;
                    string appName = MediaPlayerData.GetAndCacheMediaPlayerData(appId).Item1 ?? appId;
                    PlayerNameText.Text = appName;
                }
                else
                {
                    PlayerSwitcherPanel.Visibility = Visibility.Collapsed;
                }

                // background blurred image
                if (SettingsManager.Current.MediaFlyoutBackgroundBlur != 0)
                {
                    // make image 1:1 aspect ratio so gradient masks work for non-square images
                    var croppedImage = BitmapHelper.CropToSquare(image);

                    switch (SettingsManager.Current.MediaFlyoutBackgroundBlur)
                    {
                        case 1:
                            BackgroundImageStyle1.Source = croppedImage;
                            break;
                        case 2:
                            BackgroundImageStyle2.Source = croppedImage;
                            break;
                        case 3:
                            BackgroundImageStyle3.Source = croppedImage;
                            break;
                    }
                }

                if (SongImage.ImageSource == null) SongImagePlaceholder.Visibility = Visibility.Visible;
                else SongImagePlaceholder.Visibility = Visibility.Collapsed;

                if (_seekBarEnabled)
                {
                    var timeline = controlSession.GetTimelineProperties();

                    // State tracking
                    bool mediaSessionSupportsSeekbar = timeline.MaxSeekTime.TotalSeconds >= 1.0; // Heuristics

                    if (_mediaSessionSupportsSeekbar != mediaSessionSupportsSeekbar)
                    {
                        _mediaSessionSupportsSeekbar = mediaSessionSupportsSeekbar;
                        UpdateUILayout();
                        // Force refly
                        _isHiding = true;
                        ShowMediaFlyout();
                    }

                    if (mediaSessionSupportsSeekbar)
                    {
                        Seekbar.Maximum = timeline.MaxSeekTime.TotalSeconds;
                        SeekbarMaxDuration.Text = timeline.MaxSeekTime.ToString(timeline.MaxSeekTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
                    }
                }
            }
        });
    }

    private void PlayerPrevButton_Click(object sender, RoutedEventArgs e)
    {
        CycleMediaSession(next: false);
        var session = GetActiveMediaSession();
        if (session != null) UpdateUI(session);
    }

    private void PlayerNextButton_Click(object sender, RoutedEventArgs e)
    {
        CycleMediaSession(next: true);
        var session = GetActiveMediaSession();
        if (session != null) UpdateUI(session);
    }

    private DateTime _flyoutLastWheelCycle = DateTime.MinValue;

    private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var validSessions = GetValidMediaSessions();
        if (validSessions.Count <= 1) return;

        var now = DateTime.UtcNow;
        if ((now - _flyoutLastWheelCycle).TotalMilliseconds < 100) return;
        _flyoutLastWheelCycle = now;

        CycleMediaSession(next: e.Delta < 0);
        var active = GetActiveMediaSession();
        if (active != null) UpdateUI(active);
    }

    private void UpdateUILayout() // update the layout based on the settings
    {
        Dispatcher.Invoke(() =>
        {
            int extraWidth = SettingsManager.Current.RepeatEnabled ? 36 : 0;
            extraWidth += SettingsManager.Current.ShuffleEnabled ? 36 : 0;
            extraWidth += SettingsManager.Current.PlayerInfoEnabled ? 72 : 0;
            // keep minimum width at 72 even if all extra features are disabled to prevent the widget from being too small
            extraWidth = Math.Max(extraWidth, 72);

            int extraHeight = SettingsManager.Current.SeekbarEnabled && _mediaSessionSupportsSeekbar ? 36 : 0;

            if (SettingsManager.Current.CompactLayout) // compact layout
            {
                Height = 60 + extraHeight;
                Width = 400;
                BodyStackPanel.Orientation = Orientation.Horizontal;
                BodyStackPanel.Width = 300;
                ControlsStackPanel.Margin = new Thickness(0);
                ControlsStackPanel.Width = 104;
                MediaIdStackPanel.Visibility = Visibility.Collapsed;
                SongImageBorder.Margin = new Thickness(0);
                SongImageBorder.Height = 36;
                SongInfoStackPanel.Margin = new Thickness(8, 0, 0, 0);
                SongInfoStackPanel.Width = 182;
                if (SettingsManager.Current.MediaFlyoutAlwaysDisplay)
                {
                    SongInfoStackPanel.Width -= 36;
                    ControlsStackPanel.Width += 44;
                }
            }
            else // normal layout
            {
                Height = 112 + extraHeight;
                Width = 310 - 72 + extraWidth;
                BodyStackPanel.Orientation = Orientation.Vertical;
                BodyStackPanel.Width = 194 - 72 + extraWidth;
                ControlsStackPanel.Margin = Margin = new Thickness(12, 8, 0, 0);
                ControlsStackPanel.Width = 184 - 72 + extraWidth;
                MediaIdStackPanel.Visibility = Visibility.Visible;
                SongImageBorder.Margin = new Thickness(6);
                SongImageBorder.Height = 78;
                SongInfoStackPanel.Margin = new Thickness(12, 0, 0, 0);
                SongInfoStackPanel.Width = 182 - 72 + extraWidth;
            }

            if (SettingsManager.Current.CenterTitleArtist)
            {
                SongTitle.HorizontalAlignment = HorizontalAlignment.Center;
                SongArtist.HorizontalAlignment = HorizontalAlignment.Center;
            }
            else
            {
                SongTitle.HorizontalAlignment = HorizontalAlignment.Left;
                SongArtist.HorizontalAlignment = HorizontalAlignment.Left;
            }

            if (SettingsManager.Current.SeekbarEnabled)
                SeekbarWrapper.Visibility = Visibility.Visible;
            else
                SeekbarWrapper.Visibility = Visibility.Collapsed;
        });

        _layout = SettingsManager.Current.CompactLayout;
        _repeatEnabled = SettingsManager.Current.RepeatEnabled;
        _shuffleEnabled = SettingsManager.Current.ShuffleEnabled;
        _playerInfoEnabled = SettingsManager.Current.PlayerInfoEnabled;
        _centerTitleArtist = SettingsManager.Current.CenterTitleArtist;
        _seekBarEnabled = SettingsManager.Current.SeekbarEnabled;
        _alwaysDisplay = SettingsManager.Current.MediaFlyoutAlwaysDisplay;
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;

        await activeSession.ControlSession.TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;

        await activeSession.ControlSession.TryTogglePlayPauseAsync();
    }

    private async void Forward_Click(object sender, RoutedEventArgs e)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;

        await activeSession.ControlSession.TrySkipNextAsync();
    }

    private async void Repeat_Click(object sender, RoutedEventArgs e)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;

        if (activeSession.ControlSession.GetPlaybackInfo().AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.None)
        {
            SymbolRepeat.Dispatcher.Invoke(() => SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAll24);
            await activeSession.ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.List);
        }
        else if (activeSession.ControlSession.GetPlaybackInfo().AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.List)
        {
            SymbolRepeat.Dispatcher.Invoke(() => SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeat124);
            await activeSession.ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.Track);
        }
        else if (activeSession.ControlSession.GetPlaybackInfo().AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.Track)
        {
            SymbolRepeat.Dispatcher.Invoke(() => SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAllOff24);
            await activeSession.ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.None);
        }
    }

    private async void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;

        if (activeSession.ControlSession.GetPlaybackInfo().IsShuffleActive == true)
        {
            SymbolShuffle.Dispatcher.Invoke(() => SymbolShuffle.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24);
            await activeSession.ControlSession.TryChangeShuffleActiveAsync(false);
        }
        else
        {
            SymbolShuffle.Dispatcher.Invoke(() => SymbolShuffle.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24);
            await activeSession.ControlSession.TryChangeShuffleActiveAsync(true);
        }
    }

    private void Seekbar_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        _isDragging = true;

        Slider slider = (Slider)sender;
        System.Windows.Point clickPosition = e.GetPosition(slider);
        double thumbWidth = slider.Template.FindName("Thumb", slider) is Thumb thumb ? thumb.ActualWidth : 0;
        double ratio = (clickPosition.X - thumbWidth / 2) / (slider.ActualWidth - thumbWidth);
        ratio = Math.Max(0, Math.Min(1, ratio));
        double targetSeconds = ratio * slider.Maximum;
        // Bug: if the position is 0, then it will cause the position to not change when changing playback position
        if (targetSeconds == 0) targetSeconds = 1;
        Dispatcher.Invoke(() =>
        {
            Seekbar.Value = TimeSpan.FromSeconds(targetSeconds).TotalSeconds;
        });
    }

    private async void Seekbar_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (GetActiveMediaSession() is { } session)
        {
            var seekPosition = TimeSpan.FromSeconds(Seekbar.Value);
            if (seekPosition == TimeSpan.Zero) seekPosition = TimeSpan.FromSeconds(1);
            await session.ControlSession.TryChangePlaybackPositionAsync(seekPosition.Ticks);
        }
        _isDragging = false;
    }

    private void Seekbar_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isDragging) return;
        var timespan = TimeSpan.FromSeconds(e.NewValue);
        Dispatcher.Invoke(() =>
        {
            SeekbarCurrentDuration.Text = timespan.ToString(timespan.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
        });
    }

    private void SeekbarUpdateUi(object? sender)
    {
        if (DateTime.Now.Subtract(_lastSelfUpdateTimestamp).TotalSeconds < 1) return;

        if (!_seekBarEnabled || Visibility != Visibility.Visible || _isDragging) return;
        if (GetActiveMediaSession() is not { } session) return;

        var timeline = session.ControlSession.GetTimelineProperties();
        var pos = timeline.Position + (DateTime.Now - timeline.LastUpdatedTime.DateTime);
        if (pos > timeline.EndTime)
        {
            HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        UpdateSeekbarCurrentDuration(pos);
    }

    private void UpdateSeekbarCurrentDuration(TimeSpan pos)
    {
        Dispatcher.Invoke(() =>
        {
            Seekbar.Value = pos.TotalSeconds;
            SeekbarCurrentDuration.Text = pos.ToString(pos.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
        });
    }

    private void HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status)
    {
        if (status == null) return;
        if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            if (_isActive) return;
            _isActive = true;
            _positionTimer.Change(0, _seekbarUpdateInterval);
        }
        else
        {
            if (!_isActive) return;
            _isActive = false;
            _positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void CleanupResources()
    {
        // try saving settings before exiting if window is still open
        // disabled because it caused too many issues (race conditions, shutdown exceptions), could look into another time
        //try
        //{
        //    SettingsManager.SaveSettings();
        //    Logger.Info("Settings saved successfully on cleanup");
        //}
        //catch (Exception ex)
        //{
        //    Logger.Error(ex, "Error while saving settings on cleanup");
        //}

        // should be handled automatically on app exit but just in case
        try
        {
            // unsubscribe from events
            mediaManager.OnAnyMediaPropertyChanged -= MediaManager_OnAnyMediaPropertyChanged;
            mediaManager.OnAnyPlaybackStateChanged -= CurrentSession_OnPlaybackStateChanged;
            mediaManager.OnAnyTimelinePropertyChanged -= MediaManager_OnAnyTimelinePropertyChanged;
            mediaManager.OnAnySessionClosed -= MediaManager_OnAnySessionClosed;

            // dispose managed resources
            _positionTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _positionTimer?.Dispose();
            cts?.Cancel();
            cts?.Dispose();

            TaskbarVisualizerControl.DisposeVisualizer();

            // unhook hooks
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            DeregisterShellHookWindow(new WindowInteropHelper(this).Handle);

            // clean up other resources
            if (lockWindow?.IsLoaded == true)
                lockWindow.Close();

            if (nextUpWindow?.IsLoaded == true)
                nextUpWindow.Close();

            if (taskbarWindow?.IsLoaded == true)
                taskbarWindow.Close();

            if (volumeMixerWindow?.IsLoaded == true)
                volumeMixerWindow.Close();

            // restore native volume OSD
            VolumeMixerWindow.ShowVolumeOsd();

            // dispose mutex
            singleton?.Dispose();

            // flush and close NLog
            NLog.LogManager.Shutdown();
        }
        catch (ObjectDisposedException)
        {
            // harmless shutdown exceptions
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            CleanupResources();
        }
        finally
        {
            base.OnClosed(e);
        }
    }

    private void MicaWindow_MouseEnter(object sender, MouseEventArgs e) // keep the flyout open when mouse is over
    {
        ShowMediaFlyout();
    }

    private void NotifyIconQuit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CleanupResources();
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    private async Task<bool> WaitForExplorerReadyAsync(int timeoutMs = 60000)
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero &&
                GetWindowRect(taskbar, out NativeMethods.RECT rect) &&
                rect.Right > rect.Left &&
                rect.Bottom > rect.Top)
            {
                return true; // taskbar exists and has geometry
            }

            await Task.Delay(200);
        }

        return false;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // detect key presses from both keyboard hook and shell hook to show flyouts
        if (msg == WM_SHELLHOOK && wParam == HSHELL_APPCOMMAND)
        {
            int highWord = (int)(lParam >> 16);
            int cmd = highWord & 0x0FFF;
            int device = highWord & 0xF000;

            bool isMediaCommand = cmd switch
            {
                APPCOMMAND_MEDIA_PLAY_PAUSE => true,
                APPCOMMAND_MEDIA_NEXTTRACK => true,
                APPCOMMAND_MEDIA_PREVIOUSTRACK => true,
                APPCOMMAND_MEDIA_STOP => true,
                _ => false
            };

            bool isVolumeCommand = false;

            if (!isMediaCommand && !SettingsManager.Current.MediaFlyoutVolumeKeysExcluded)
            {
                isVolumeCommand = cmd switch
                {
                    APPCOMMAND_VOLUME_MUTE => true,
                    APPCOMMAND_VOLUME_DOWN => true,
                    APPCOMMAND_VOLUME_UP => true,
                    _ => false
                };
            }

            if (!isMediaCommand && !isVolumeCommand)
                return 0;

            bool isKeyCommand = device == FAPPCOMMAND_KEY;

            if (!isKeyCommand)
                return 0;

            bool result = TryShowMediaFlyoutDebounced();

            if (!result)
            {
                return 0;
            }

            handled = true;
        }
        else if (msg == WM_TASKBARCREATED)
        {
            Logger.Warn("Explorer restart detected (TaskbarCreated)");

            ExplorerRestarting = true;

            // Defer recovery, do NOT touch tray/taskbar immediately
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    // Wait for Explorer to actually stabilize
                    if (await WaitForExplorerReadyAsync())
                    {
                        ExplorerRestarting = false;
                        Logger.Info("Explorer stabilized, resuming taskbar integration");

                        // Now it is safe to recreate tray icon
                        RecreateTrayIconSafely();
                    }
                    else
                    {
                        Logger.Warn("Explorer did not stabilize within timeout; keeping integration disabled");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Explorer recovery failed");
                }
            }, DispatcherPriority.Background);

            handled = true;
            return 0;
        }
        else if (msg == WM_SETTINGCHANGE) // system settings changed
        {
            if (lParam == IntPtr.Zero)
                return 0;

            // check if the changed setting is related to theme or accent color
            string? changedSetting = Marshal.PtrToStringUni(lParam);
            if (changedSetting != "ImmersiveColorSet" && changedSetting != "WindowsThemeElement")
                return 0;

            Logger.Info($"System setting changed: {changedSetting}, from {msg}");

            try
            {
                // update theme for taskbar widget since it's independent from the main app theme
                ThemeManager.UpdateTaskbarWidget();
                // update Acrylic windows background colors
                WindowBlurHelper.AdjustBlurOpacityForAllWindows(SettingsManager.Current.AcrylicBlurOpacity);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to apply theme changes to taskbar widgets or Acrylic windows");
            }
            return 0;
        }

        return 0;
    }

    private void RecreateTrayIconSafely()
    {
        try
        {
            nIcon.Visibility = Visibility.Collapsed;

            if (!SettingsManager.Current.NIconHide)
            {
                nIcon.Visibility = Visibility.Visible;
                nIcon.Register();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to recreate tray icon safely");
        }
    }

    private async void MicaWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Hide();
        UpdateUILayout();
        ThemeManager.ApplySavedTheme();

        // add tray icon hook when taskbar resets
        try
        {
            HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
            {
                source.AddHook(WndProc);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize tray icon");
        }

        try
        {
            await LicenseManager.Instance.InitializeAsync();

            // Sync license status from LicenseManager to SettingsManager
            SettingsManager.Current.IsPremiumUnlocked = LicenseManager.Instance.IsPremiumUnlocked;
            SettingsManager.Current.IsStoreVersion = LicenseManager.Instance.IsStoreVersion;
            SettingsManager.SaveSettings();

            Logger.Info($"License synced on startup - Store: {SettingsManager.Current.IsStoreVersion}, Premium: {SettingsManager.Current.IsPremiumUnlocked}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize license");
        }

        // Add the experiments loading here
        await ExperimentsService.GetExperimentsAsync();

        BitmapHelper.GetDominantColors(1);
        taskbarWindow = new TaskbarWindow();
        UpdateTaskbar();
        volumeMixerWindow = new VolumeMixerWindow();
    }

    public void RecreateTaskbarWindow()
    {
        try
        {
            Logger.Info("Recreating Taskbar Widget window");

            if (taskbarWindow != null)
            {
                try
                {
                    taskbarWindow.Close();
                }
                catch { }

                taskbarWindow = null;
            }

            taskbarWindow = new();
            UpdateTaskbar();

            Logger.Info("Taskbar Widget window recreated successfully");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to recreate Taskbar Widget window");
        }
    }

    private ClipboardHistoryWindow? clipboardHistoryWindow;

    public void ShowVolumeMixerFromTaskbar(Rect anchorRect, Rect taskbarRect)
    {
        volumeMixerWindow ??= new VolumeMixerWindow();
        volumeMixerWindow.ShowFromTaskbar(anchorRect, taskbarRect);
    }

    public void ToggleClipboardHistoryFromTaskbar(Rect anchorRect, Rect taskbarRect, IntPtr returnFocusWindow)
    {
        clipboardHistoryWindow ??= new ClipboardHistoryWindow(this);
        clipboardHistoryWindow.ToggleFromTaskbar(anchorRect, taskbarRect, returnFocusWindow);
    }

    private float _volumeScrollAccumulator = 0f;

    public void AdjustMasterVolumeFromWheel(int delta)
    {
        volumeMixerWindow ??= new VolumeMixerWindow();

        _volumeScrollAccumulator += (delta / 120.0f) * 0.02f;
        if (MathF.Abs(_volumeScrollAccumulator) >= 0.01f)
        {
            float step = _volumeScrollAccumulator;
            _volumeScrollAccumulator = 0f;

            volumeMixerWindow.ViewModel.AdjustMasterVolume(step);

            if (!volumeMixerWindow.IsTaskbarAnchored && SettingsManager.Current.VolumeControlEnabled)
            {
                volumeMixerWindow.ShowFlyout();
            }
        }
    }

    private void nIcon_LeftClick(Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e) // change the behavior of the tray icon
    {
        if (SettingsManager.Current.NIconLeftClick == 0)
        {
            openSettings(sender, e);
            //Wpf.Ui.Appearance.ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica); // to change the theme
            //ThemeService themeService = new ThemeService();
            //themeService.ChangeTheme(MicaWPF.Core.Enums.WindowsTheme.Light);
        }
        else if (SettingsManager.Current.NIconLeftClick == 1) ShowMediaFlyout();
    }

    private Task PauseOtherSessions(MediaSession currentMediaSession)
    {
        return Task.WhenAll(
            mediaManager.CurrentMediaSessions.Values.Select(session =>
            {
                if (
                    session.Id != currentMediaSession.Id &&
                    session.ControlSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                )
                {
                    return session.ControlSession.TryPauseAsync().AsTask();
                }
                return Task.CompletedTask;
            })
        );
    }
    internal void ToggleBlur()
    {
        if (SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled)
        {
            WindowBlurHelper.EnableBlur(this);
        }
        else
        {
            WindowBlurHelper.DisableBlur(this);
        }
    }

    private void MediaFlyoutCloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Use the updated ShowMediaFlyout method with toggle mode to close the flyout
        ShowMediaFlyout(toggleMode: true);
    }
}