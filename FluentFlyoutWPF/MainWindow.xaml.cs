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
    private Thread? _hookThread;
    private uint _hookThreadId;

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

    /// <summary>
    /// True when another same-named process started very recently (boot/login
    /// storms, duplicate autostart entries). Used to swallow the
    /// open-settings signal such launches would otherwise trigger.
    /// </summary>
    private static bool IsFirstInstanceStartingUp()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            foreach (var proc in Process.GetProcessesByName(me.ProcessName))
            {
                try
                {
                    if (proc.Id != me.Id &&
                        (DateTime.UtcNow - proc.StartTime.ToUniversalTime()).TotalSeconds < 30)
                        return true;
                }
                catch
                {
                    // process exited or start time unreadable; ignore
                }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to check first-instance startup age");
        }
        return false;
    }
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

    private readonly DispatcherTimer _displayRefreshTimer;
    private string _pendingDisplayRefreshReason = "Unknown";
    private bool _displayRefreshInProgress;
    private bool _isCleaningUp;

    internal static volatile bool ExplorerRestarting = false;

    public MainWindow()
    {
        DataContext = SettingsManager.Current;
        WindowHelper.SetNoActivate(this); // prevents some fullscreen apps from minimizing
        InitializeComponent();
        WindowHelper.SetTopmost(this); // more prevention of fullscreen apps minimizing

        _displayRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        _displayRefreshTimer.Tick += DisplayRefreshTimer_Tick;

        if (!singleton.WaitOne(TimeSpan.Zero, true)) // if another instance is already running, close this one
        {
            // A second launch with no live user behind it (duplicate autostart
            // entries, boot storms, updater re-launch) must not pop the
            // settings window on the running instance (#1029: window appearing
            // on every boot). Only forward the open-settings signal when the
            // first instance is past its startup window.
            if (IsFirstInstanceStartingUp())
            {
                Logger.Info("Duplicate launch during first-instance startup; exiting silently without opening settings.");
                Environment.Exit(0);
            }

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
        // Install the low-level keyboard hook on a dedicated thread with its
        // own message pump. Hook callbacks run on the installing thread, so
        // installing on the UI thread delayed every keystroke system-wide
        // whenever the UI thread was busy (song-info/thumbnail fetches),
        // producing keyboard-only input lag with no CPU spike (#1083).
        _hookThread = new Thread(HookThreadProc) { IsBackground = true, Name = "FluentFlyout Keyboard Hook" };
        _hookThread.Start();

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

        string previousVersion = SettingsManager.Current.LastKnownVersion;
        _ = CheckForExperimentsOnStartupAsync(previousVersion);

        // apply other things on new thread
        Dispatcher.Invoke(() =>
        {
            LocalizationManager.ApplyLocalization();

            try // update last known version. gets the version of the app, works only in release mode
            {
                var version = Package.Current.Id.Version;
                SettingsManager.Current.LastKnownVersion = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                SettingsManager.Current.LastKnownVersion = "debug";
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
            if (SettingsManager.Current.BlockedApps != null &&
                SettingsManager.Current.BlockedApps.Any(b => MatchesFilterEntry(b, appName, appId)))
                return false;

            return true;
        }
        else // Whitelist mode
        {
            if (SettingsManager.Current.AllowedApps != null &&
                SettingsManager.Current.AllowedApps.Any(a => MatchesFilterEntry(a, appName, appId)))
                return true;

            return false;
        }
    }

    // display names are matched in full: entries always hold a whole app name, and a substring match let one entry
    // swallow others (blocking "Amazon Music" also blocked "Amazon Music SMTC Bridge"). the session id keeps substring
    // matching so an entry can still target a whole package or publisher
    private static bool MatchesFilterEntry(string entry, string appName, string appId)
    {
        if (string.IsNullOrWhiteSpace(entry)) return false; // an empty entry would match every session

        return appName.Equals(entry, StringComparison.OrdinalIgnoreCase) || appId.Contains(entry, StringComparison.OrdinalIgnoreCase);
    }

    public MediaSession? GetActiveMediaSession()
    {
        var validSessions = mediaManager.CurrentMediaSessions.Values.Where(IsSessionAllowed).ToList();

        if (validSessions.Count == 0) return null;

        var focused = mediaManager.GetFocusedSession();
        if (focused != null && validSessions.Any(s => s.Id == focused.Id))
            return focused;

        return validSessions.FirstOrDefault();
    }

    public float? GetActiveMediaAppVolume()
    {
        if (volumeMixerWindow?.ViewModel is not { } volumeMixerViewModel ||
            GetActiveMediaSession() is not { } activeSession)
            return null;

        int? processId = MediaPlayerData.GetAndCacheProcessId(activeSession.Id);
        return processId.HasValue
            ? volumeMixerViewModel.Sessions.FirstOrDefault(session => session.ProcessId == processId.Value)?.Volume
            : null;
    }

    public void AdjustTaskbarVolume(float delta)
    {
        if (volumeMixerWindow?.ViewModel is not { } volumeMixerViewModel) return;

        switch (SettingsManager.Current.TaskbarWidgetScrollVolumeMode)
        {
            // 0 = disabled, 1 = master volume, 2 = active media session
            case 1:
                bool success = volumeMixerViewModel.TryAdjustMasterVolume(delta);

                if (success && SettingsManager.Current.VolumeControlEnabled)
                    volumeMixerWindow?.ShowFlyout();
                break;

            case 2:
                if (GetActiveMediaSession() is not { } activeSession) return;

                int? processId = MediaPlayerData.GetAndCacheProcessId(activeSession.Id);
                if (processId.HasValue)
                {
                    volumeMixerViewModel.TryAdjustSessionVolume(processId.Value, delta);
                }
                break;
        }
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
            }
        }
    }

    public async Task<bool> TrySkipPreviousAsync()
    {
        var focusedSession = GetActiveMediaSession();
        if (focusedSession == null) return false;
        return await focusedSession.ControlSession.TrySkipPreviousAsync();
    }

    public async Task<bool> TryTogglePlayPauseAsync()
    {
        var focusedSession = GetActiveMediaSession();
        if (focusedSession == null) return false;
        return await focusedSession.ControlSession.TryTogglePlayPauseAsync();
    }

    public async Task<bool> TrySkipNextAsync()
    {
        var focusedSession = GetActiveMediaSession();
        if (focusedSession == null) return false;
        return await focusedSession.ControlSession.TrySkipNextAsync();
    }

    public async Task<bool> TryOpenMediaPlayerAsync()
    {
        try
        {
            if (GetActiveMediaSession() is { } activeSession)
            {
                var mediaProperties = TryGetMediaProperties(activeSession.ControlSession);
                return await Task.Run(() => MediaPlayerData.TryActivateMediaPlayer(activeSession.Id, mediaProperties?.Title));
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open media player");
        }
        return false;
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
    private static double GetBottomCenterFlyoutBottomMargin(bool reserveNativeVolumeOsdSpace)
    {
        if (!reserveNativeVolumeOsdSpace)
            return 16 + GetAutoHideTaskbarInset(bottomEdge: true);

        return (SettingsManager.Current.VolumeControlEnabled && SettingsManager.Current.VolumeControlAboveMediaFlyout ? 16 : 80)
            + GetAutoHideTaskbarInset(bottomEdge: true);
    }

    /// <summary>
    /// Extra margin (raw pixels) to keep free for an auto-hidden taskbar on the
    /// given screen edge. An auto-hidden taskbar is not part of the monitor work
    /// area, so flyouts anchored 16px from the edge are covered by (or cover) the
    /// taskbar as soon as it slides out (#987, #1039).
    /// Returns 0 when the taskbar is not in auto-hide mode or lives on another edge.
    /// </summary>
    private static double GetAutoHideTaskbarInset(bool bottomEdge)
    {
        try
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            int state = (int)SHAppBarMessage(ABM_GETSTATE, ref data);
            if ((state & ABS_AUTOHIDE) == 0)
                return 0;

            var posData = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            if (SHAppBarMessage(ABM_GETTASKBARPOS, ref posData) == IntPtr.Zero)
                return 0;

            // ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3
            bool taskbarOnBottom = posData.uEdge == 3;
            bool taskbarOnTop = posData.uEdge == 1;
            if (bottomEdge ? !taskbarOnBottom : !taskbarOnTop)
                return 0;

            double height = posData.rc.Bottom - posData.rc.Top;
            if (height <= 0 || height > 500)
                return 0;

            return height;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to query auto-hide taskbar state");
            return 0;
        }
    }

    private (double left, double top) GetFinalPosition(Rect windowRect, Rect workArea, bool reserveNativeVolumeOsdSpace = false)
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
            0 or 2 => workArea.Top + workArea.Height - windowRect.Height - 16 - GetAutoHideTaskbarInset(bottomEdge: true),
            1 => workArea.Top + workArea.Height - windowRect.Height - GetBottomCenterFlyoutBottomMargin(reserveNativeVolumeOsdSpace),
            _ => workArea.Top + 16 + GetAutoHideTaskbarInset(bottomEdge: false)
        };
        return (left, top);
    }

    public void OpenAnimation(MicaWindow window, bool alwaysBottom = false, MonitorInfo? selectedMonitor = null, MicaWindow? aboveReference = null, bool reserveNativeVolumeOsdSpace = false)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers.Actions[0] as BeginStoryboard;
        var storyboard = beginStoryboard.Storyboard;

        DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];
        var monitor = selectedMonitor != null ? selectedMonitor.Value : getSelectedMonitor();
        var workArea = monitor.workArea;

        // prevent flickering
        WindowHelper.SetVisibility(window, false); // window.Visibility = Visibility.Hidden works with some delay

        // Update the DPI by moving the window to the target workArea, ignoring WPF scaling
        WindowHelper.SetPosition(window, workArea.Left, workArea.Top);
        var windowRect = WindowHelper.GetPlacement(window); // here we take the updated window size in raw coordinates.

        double window_left = 0;

        // If a reference window is provided and visible, position the window next to it
        if (aboveReference != null && aboveReference.IsVisible)
        {
            // Here we work with raw monitor coordinates, without taking DPI into account.
            double refWidth = aboveReference.Width * monitor.dpiX / 96.0;
            double refHeight = aboveReference.Height * monitor.dpiY / 96.0;
            var refRect = new Rect(0, 0, refWidth, refHeight);
            var (refLeft, refTop) = GetFinalPosition(refRect, workArea, reserveNativeVolumeOsdSpace);

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
            if (_position < 0 || _position > 5)
                _position = 1; // corrupted setting: fall back to bottom-center so the flyout always lands on-screen
            if (_position == 0)
            {
                window_left = workArea.Left + 16;
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16 - GetAutoHideTaskbarInset(bottomEdge: true);
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0) // if off, don't animate (just appear at the bottom)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height + 4; // appear from the bottom of the screen
            }
            else if (_position == 1)
            {
                window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
                double bottomMargin = GetBottomCenterFlyoutBottomMargin(reserveNativeVolumeOsdSpace);
                double moveTo = workArea.Top + workArea.Height - windowRect.Height - bottomMargin;
                moveAnimation.To = moveTo;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveTo;
                else
                    moveAnimation.From = moveTo + 20;
            }
            else if (_position == 2)
            {
                window_left = workArea.Left + workArea.Width - windowRect.Width - 16;
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16 - GetAutoHideTaskbarInset(bottomEdge: true);
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height + 4;
            }
            else if (_position == 3)
            {
                window_left = workArea.Left + 16;
                moveAnimation.To = workArea.Top + 16 + GetAutoHideTaskbarInset(bottomEdge: false);
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + -4;
            }
            else if (_position == 4)
            {
                window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
                moveAnimation.To = workArea.Top + 16 + GetAutoHideTaskbarInset(bottomEdge: false);
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + -4;
            }
            else if (_position == 5)
            {
                window_left = workArea.Left + workArea.Width - windowRect.Width - 16;
                moveAnimation.To = workArea.Top + 16 + GetAutoHideTaskbarInset(bottomEdge: false);
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
            moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16 - GetAutoHideTaskbarInset(bottomEdge: true);
            if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                moveAnimation.From = moveAnimation.To;
            else
                moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height + 4;
        }

        // Set the initial position in raw coordinates.
        WindowHelper.SetPosition(window, window_left, moveAnimation.From!.Value);

        // Capture the exact resting position in raw pixels before converting to
        // DIPs below; used for the post-animation correction.
        double restLeftRaw = window_left;
        double restTopRaw = moveAnimation.To ?? moveAnimation.From.Value;

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

        Logger.Info($"Flyout '{window.GetType().Name}' shown at raw ({restLeftRaw:0}, {restTopRaw:0}) on '{monitor.deviceName}' ({monitor.workArea.Width:0}x{monitor.workArea.Height:0}@{monitor.workArea.Left:0},{monitor.workArea.Top:0} dpiY={monitor.dpiY})");

        // Guarantee the resting position in raw pixels once the flight ends. On
        // mixed-DPI multi-monitor setups the DIP scaling above can race the
        // window's DPI flip, stranding the flyout off-screen so it never
        // appears. The async correction is a no-op when already correct.
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(msDuration + 150); } catch { return; }
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (!window.IsVisible)
                            return;
                        WindowHelper.SetPosition(window, restLeftRaw, restTopRaw, async: true);
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Flyout resting-position correction failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Flyout resting-position dispatch failed");
            }
        });
    }

    public void CloseAnimation(MicaWindow window, MonitorInfo? selectedMonitor = null)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers.Actions[0] as BeginStoryboard;
        var storyboard = beginStoryboard.Storyboard;

        DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];
        var monitor = selectedMonitor != null ? selectedMonitor.Value : getSelectedMonitor();
        var workArea = monitor.workArea;

        // Use the window's actual current rendered position as the animation
        // start. GetWindowPlacement can report zeros for transient handles and
        // the selected monitor's DPI may differ from the monitor hosting the
        // flyout - either made From wrong, so the first frame snapped
        // (flyout jumps upward) before animating downward.
        double dpiY = monitor.dpiY;
        double currentTopPhys = double.NaN;
        try
        {
            currentTopPhys = window.PointToScreen(new Point(0, 0)).Y;
            var host = MonitorUtil.GetMonitor(window);
            if (host.dpiY > 0)
                dpiY = host.dpiY;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "CloseAnimation PointToScreen failed, falling back to placement");
        }
        var placementRect = WindowHelper.GetPlacement(window);
        if (double.IsNaN(currentTopPhys))
            currentTopPhys = placementRect.Top;
        if (dpiY <= 0)
            dpiY = 96;

        // Use the window's actual current position as the animation start
        moveAnimation.From = currentTopPhys * 96.0 / dpiY;

        // Determine slide direction (physical pixels throughout)
        bool isTopHalf = currentTopPhys + placementRect.Height / 2 < workArea.Top + workArea.Height / 2;
        if (SettingsManager.Current.FlyoutAnimationSpeed != 0)
        {
            moveAnimation.To = moveAnimation.From + (isTopHalf ? -20 : 20);
        }
        else
        {
            // Animations off: still pin To to From so a stale storyboard value
            // can't teleport the window on the zero-duration snap.
            moveAnimation.To = moveAnimation.From;
        }

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

    public void UpdateTaskbar()
    {
        var activeSession = GetActiveMediaSession();
        if (!mediaManager.IsStarted || activeSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var songInfo = TryGetMediaProperties(activeSession.ControlSession);
        if (songInfo == null)
            return;

        var playbackInfo = activeSession.ControlSession.GetPlaybackInfo();
        var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        BitmapHelper.GetDominantColors(1);
        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus, playbackInfo.Controls);
    }

    public void reportBug(object? sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/unchihugo/FluentFlyout/issues/new/choose",
            UseShellExecute = true
        });
    }

    private void openRepository(object? sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/unchihugo/FluentFlyout",
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
        if (!SettingsManager.Current.PauseOtherSessionsEnabled)
            return;

        // Only the session the user actually interacted with may pause the
        // others. This ran for every session that raised an event, so a
        // background player emitting a property/playback update paused the
        // session the user had just started, which then paused the first one
        // back - the "exclusive audio mode" ping-pong between e.g. Spotify and
        // a browser video (#1084).
        if (GetActiveMediaSession() is not { } activeSession || activeSession.Id != mediaSession.Id)
            return;

        if (mediaSession.ControlSession?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            PauseOtherSessions(mediaSession);
        }
    }

    private void CurrentSession_OnPlaybackStateChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null)
    {
#if DEBUG
        Logger.Debug("Playback state changed: " + mediaSession.Id + " " + mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
#endif     
        pauseOtherMediaSessionsIfNeeded(mediaSession);

        var focusedSession = GetActiveMediaSession();
        if (focusedSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var tbSongInfo = TryGetMediaProperties(focusedSession.ControlSession);
        if (tbSongInfo != null)
        {
            var tbThumbnail = BitmapHelper.GetThumbnail(tbSongInfo.Thumbnail);
            BitmapHelper.GetDominantColors(1);
            var tbPlayback = focusedSession.ControlSession.GetPlaybackInfo();

            taskbarWindow?.UpdateUi(tbSongInfo.Title, tbSongInfo.Artist, tbThumbnail, tbPlayback?.PlaybackStatus, tbPlayback?.Controls);
        }

        if (IsVisible)
        {
            UpdateUI(focusedSession);
            HandlePlayBackState(playbackInfo?.PlaybackStatus);
        }
    }

    // for determining whether MediaPropertyChanged has no changes
    private string previousMediaProperty = "";
    private int previousMediaPropertyThumbnail = 0;
    private void MediaManager_OnAnyMediaPropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
    {
        // sometimes mediaSession.ControlSession can be null
        if (mediaSession.ControlSession == null)
            return;

#if DEBUG
        Logger.Debug("Media property changed: " + mediaProperties.Title + " " + mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
#endif
        var currentActiveSession = GetActiveMediaSession();
        if (currentActiveSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var songInfo = TryGetMediaProperties(currentActiveSession.ControlSession);
        if (songInfo == null)
            return;

        var playbackInfo = currentActiveSession.ControlSession.GetPlaybackInfo();

        // Players republish empty metadata for a moment when a track restarts
        // (looping a song in YouTube Music) before sending the real properties.
        // Pushing that blank snapshot left the taskbar widget showing only the
        // album art with no title/artist until the song was changed manually,
        // and poisoned the dedupe cache so the real update was suppressed
        // (#961). Ignore the blank interim state while the session is alive.
        if (string.IsNullOrWhiteSpace(songInfo.Title) && string.IsNullOrWhiteSpace(songInfo.Artist)
            && playbackInfo.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed
            && playbackInfo.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped)
        {
            return;
        }

        string check = songInfo.Title + songInfo.Artist + playbackInfo.PlaybackStatus;
        int checkThumbnail = BitmapHelper.GetStableThumbnailHash(songInfo.Thumbnail);
        bool onlyThumbnailChanged = false;
        if (previousMediaProperty == check)
        {
            onlyThumbnailChanged = true;
            if (previousMediaPropertyThumbnail == checkThumbnail)
                return; // prevent multiple calls for the same song info
        }

        previousMediaProperty = check;
        previousMediaPropertyThumbnail = checkThumbnail;

        var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        BitmapHelper.GetDominantColors(1);

        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus, playbackInfo.Controls);

        pauseOtherMediaSessionsIfNeeded(mediaSession);

        if (SettingsManager.Current.NextUpEnabled && !FullscreenDetector.IsFullscreenApplicationRunning()) // show NextUpWindow if enabled in settings
        {
            void createNewNextUpWindow()
            {
                Dispatcher.Invoke(() =>
                {
                    if (nextUpWindow == null && playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) // double-check within the Dispatcher to prevent race conditions
                    {
                        nextUpWindow = new NextUpWindow(songInfo.Title, songInfo.Artist, thumbnail);
                        currentTitle = songInfo.Title;
                        nextUpWindow.Closed += (s, e) => nextUpWindow = null; // set nextUpWindow to null when closed
                    }
                });
            }

            // A looped track legitimately repeats its title, so comparing against
            // the last shown title alone permanently suppressed the next-up
            // flyout for that song (#961). Allow a repeat once the title has
            // actually been republished by the player.
            bool isNewSong = currentTitle != songInfo.Title || !onlyThumbnailChanged;

            if (nextUpWindow == null && IsVisible == false && songInfo.Thumbnail != null && isNewSong)
            {
                createNewNextUpWindow();
            }
            else if (nextUpWindow != null && !onlyThumbnailChanged)
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
            else if (nextUpWindow != null && songInfo.Thumbnail != null)
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

    /// <summary>
    /// Installs the keyboard hook and pumps messages on the dedicated hook
    /// thread until shutdown. Never touches UI objects here: callbacks must
    /// return to Windows immediately and marshal UI work via the Dispatcher.
    /// </summary>
    private void HookThreadProc()
    {
        try
        {
            _hookThreadId = GetCurrentThreadId();
            _hookId = SetHook(_hookProc);
            while (_hookId != IntPtr.Zero && GetMessage(out FluentFlyout.Classes.NativeMethods.MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Keyboard hook thread failed");
        }
        finally
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }

    private void StopHookThread()
    {
        try
        {
            uint threadId = _hookThreadId;
            _hookThreadId = 0;
            if (threadId != 0)
                PostThreadMessage(threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            // The thread unhooks itself on exit; bounded wait so shutdown
            // never hangs if the pump is stuck.
            _hookThread?.Join(TimeSpan.FromSeconds(3));
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to stop keyboard hook thread");
        }
        finally
        {
            _hookThread = null;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_KEYUP))
        {
            int vkCode = Marshal.ReadInt32(lParam);

            bool mediaKeysPressed = vkCode == 0xB3 || vkCode == 0xB0 || vkCode == 0xB1 || vkCode == 0xB2; // Play/Pause, next, previous, stop
            bool volumeKeysPressed = vkCode == 0xAD || vkCode == 0xAE || vkCode == 0xAF; // Mute, Volume Down, Volume Up

            // MainWindow.WndProc() also handles media and volume keys.
            // NOTE: a low-level keyboard hook must return to Windows immediately.
            // ShowMediaFlyout does blocking UI/COM work (Dispatcher.Invoke, media
            // property fetches, animations); doing it synchronously here stalls
            // every keystroke system-wide and Windows silently drops slow hooks,
            // which breaks Alt+Tab and the media keys themselves over time
            // (worse at boot when everything is slow). Queue it instead.
            if (mediaKeysPressed || volumeKeysPressed)
            {
                if (mediaKeysPressed || (!SettingsManager.Current.MediaFlyoutVolumeKeysExcluded && volumeKeysPressed))
                {
                    long currentTime = Environment.TickCount64;
                    // debounce to prevent hangs with rapid key presses
                    if ((currentTime - _lastFlyoutTime) >= 500) // 500ms debounce time
                    {
                        _lastFlyoutTime = currentTime;
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            try { ShowMediaFlyout(); }
                            catch (Exception ex) { Logger.Debug(ex, "Show media flyout from hook failed"); }
                        });
                    }
                }

                if (SettingsManager.Current.VolumeControlEnabled && volumeMixerWindow != null)
                {
                    // SyncMasterFromDevice is dispatcher-aware (marshals to UI thread itself).
                    // ShowFlyout is queued to the UI thread so this hook returns to
                    // Windows immediately. The hook fires before the OS applies the
                    // volume step, so the live OnVolumeNotification subscription in
                    // VolumeMixerViewModel is the source of truth for the final value.
                    var mixerWindow = volumeMixerWindow;
                    try
                    {
                        mixerWindow.ViewModel.SyncMasterFromDevice();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Volume sync from hook failed");
                    }
                    try
                    {
                        if (Dispatcher.CheckAccess())
                        {
                            mixerWindow.ShowFlyout();
                        }
                        else
                        {
                            _ = Dispatcher.BeginInvoke(() =>
                            {
                                try { mixerWindow.ShowFlyout(); }
                                catch (Exception ex) { Logger.Debug(ex, "Show volume flyout from hook failed"); }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Failed to dispatch volume flyout");
                    }
                }
            }

            if (SettingsManager.Current.LockKeysEnabled
                && !FullscreenDetector.IsFullscreenApplicationRunning()
                && wParam == WM_KEYUP)
            {
                // Window creation and resource/keyboard-state reads are
                // UI-thread affine: the hook now runs on its own thread, so
                // marshal the whole presentation step to the Dispatcher.
                (string resourceKey, Key toggleKey)? lockInfo = vkCode switch
                {
                    0x14 when SettingsManager.Current.LockKeysCapsEnabled => ("LockWindow_CapsLock", Key.CapsLock),
                    0x90 when SettingsManager.Current.LockKeysNumEnabled => ("LockWindow_NumLock", Key.NumLock),
                    0x91 when SettingsManager.Current.LockKeysScrollEnabled => ("LockWindow_ScrollLock", Key.Scroll),
                    0x2D when SettingsManager.Current.LockKeysInsertEnabled => ("Insert", Key.Insert),
                    _ => null
                };
                if (lockInfo is { } info)
                {
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            lockWindow ??= new LockWindow();
                            // "Insert" has no matching resource, so skip the lookup; fall
                            // back to the key itself if the resource is ever missing.
                            object? resource = info.resourceKey == "Insert" ? null : FindResource(info.resourceKey);
                            string label = resource?.ToString() ?? info.resourceKey;
                            lockWindow.ShowLockFlyout(label, Keyboard.IsKeyToggled(info.toggleKey));
                        }
                        catch (Exception ex) { Logger.Debug(ex, "Show lock flyout from hook failed"); }
                    });
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

    public async void ShowMediaFlyout(bool toggleMode = false, bool forceShow = false)
    {
        var activeSession = GetActiveMediaSession();
        bool flyoutEnabled = forceShow || SettingsManager.Current.MediaFlyoutEnabled;
        bool fullscreenBlock = FullscreenDetector.IsFullscreenApplicationRunning();
        if (activeSession == null || !flyoutEnabled || fullscreenBlock)
        {
            Logger.Info($"ShowMediaFlyout suppressed: session={(activeSession == null ? "none" : activeSession.Id)}, enabled={flyoutEnabled}, fullscreenBlock={fullscreenBlock}");
            return;
        }

        // If in toggle mode and flyout is visible, close it
        if (toggleMode && Visibility == Visibility.Visible && !_isHiding)
        {
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

        UpdateUI(activeSession);
        if (_seekBarEnabled)
            HandlePlayBackState(activeSession.ControlSession.GetPlaybackInfo().PlaybackStatus);

        if (nextUpWindow != null) // close NextUpWindow if it's open
        {
            nextUpWindow.Close();
            nextUpWindow = null;
        }

        if (_isHiding == true)
        {
            _isHiding = false;
            OpenAnimation(this, reserveNativeVolumeOsdSpace: true);
        }
        cts.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        Visibility = Visibility.Visible;
        WindowHelper.SetTopmost(this);

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token); // check if mouse is over every 100ms

                bool mouseOverMedia = WindowHelper.IsMouseOverWindow(this);
                bool mouseOverVolume = SettingsManager.Current.VolumeControlAboveMediaFlyout
                    && SettingsManager.Current.VolumeControlEnabled
                    && volumeMixerWindow != null
                    && volumeMixerWindow.IsVisible
                    && WindowHelper.IsMouseOverWindow(volumeMixerWindow); // sync with VolumeMixerWindow

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
        catch (Exception ex)
        {
            // Never let the auto-hide loop take the process down: an async void
            // throw here is an abnormal exit with no further logging.
            Logger.Error(ex, "Media flyout loop failed, hiding flyout");
            try
            {
                _isHiding = true;
                Hide();
            }
            catch (Exception hideEx)
            {
                Logger.Debug(hideEx, "Media flyout emergency hide failed");
            }
        }
    }

    private void UpdateMediaFlyoutCloseButtonVisibility()
    {
        MediaFlyoutCloseButton.Visibility = SettingsManager.Current.MediaFlyoutAlwaysDisplay && !SettingsManager.Current.CompactLayout ? Visibility.Visible : Visibility.Collapsed;
        ControlClose.Visibility = SettingsManager.Current.MediaFlyoutAlwaysDisplay && SettingsManager.Current.CompactLayout ? Visibility.Visible : Visibility.Collapsed;
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

                ControlPlayPause.Opacity = ControlPlayPause.IsEnabled ? 1 : 0.35;

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
                    MediaIdButton.Visibility = Visibility.Visible;
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
                else MediaIdButton.Visibility = Visibility.Collapsed;

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

                // set tooltip
                SongInfoStackPanel.ToolTip = string.Empty;
                SongInfoStackPanel.ToolTip += !String.IsNullOrEmpty(songInfo.Title) ? songInfo.Title : string.Empty;
                SongInfoStackPanel.ToolTip += !String.IsNullOrEmpty(songInfo.Artist) ? "\n\n" + songInfo.Artist : string.Empty;

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

                SongImagePlaceholder.Visibility = SongImage.ImageSource == null ? Visibility.Visible : Visibility.Collapsed;

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
                ControlsStackPanelContainer.Margin = new Thickness(2, 0, 0, 0);
                ControlsStackPanelContainer.Width = 104;
                ControlsStackPanelContainer.HorizontalAlignment = HorizontalAlignment.Left;
                ControlsStackPanel.HorizontalAlignment = HorizontalAlignment.Left;
                MediaIdButton.Visibility = Visibility.Collapsed;
                SongImageBorder.Margin = new Thickness(0);
                SongImageBorder.Height = 36;
                // keep the album art square: only Height was updated here, so the
                // 78px design width from XAML stayed and stretched the cover (#1019)
                SongImageBorder.Width = 36;
                SongInfoStackPanel.Margin = new Thickness(8, 0, 0, 0);
                SongInfoStackPanel.Width = 182;
                if (SettingsManager.Current.MediaFlyoutAlwaysDisplay)
                {
                    SongInfoStackPanel.Width -= 36;
                    ControlsStackPanelContainer.Width += 44;
                }
            }
            else // normal layout
            {
                bool centerControlsWithSongInfo = SettingsManager.Current.CenterTitleArtist && !SettingsManager.Current.PlayerInfoEnabled;
                Height = 112 + extraHeight;
                Width = 310 - 72 + extraWidth;
                BodyStackPanel.Orientation = Orientation.Vertical;
                BodyStackPanel.Width = 194 - 72 + extraWidth;
                ControlsStackPanelContainer.Margin = new Thickness(12, 8, 0, 0);
                ControlsStackPanelContainer.Width = double.NaN;
                ControlsStackPanelContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                ControlsStackPanel.HorizontalAlignment = centerControlsWithSongInfo ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                MediaIdButton.Visibility = Visibility.Visible;
                SongImageBorder.Margin = new Thickness(6);
                SongImageBorder.Height = 78;
                SongImageBorder.Width = 78;
                SongInfoStackPanel.Margin = new Thickness(12, 0, 0, 0);
                SongInfoStackPanel.Width = 182 - 72 + extraWidth;
            }

            var alignment = SettingsManager.Current.CenterTitleArtist ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            SongTitle.HorizontalAlignment = alignment;
            SongArtist.HorizontalAlignment = alignment;

            SeekbarWrapper.Visibility = SettingsManager.Current.SeekbarEnabled ? Visibility.Visible : Visibility.Collapsed;
        });

        _layout = SettingsManager.Current.CompactLayout;
        _repeatEnabled = SettingsManager.Current.RepeatEnabled;
        _shuffleEnabled = SettingsManager.Current.ShuffleEnabled;
        _playerInfoEnabled = SettingsManager.Current.PlayerInfoEnabled;
        _centerTitleArtist = SettingsManager.Current.CenterTitleArtist;
        _seekBarEnabled = SettingsManager.Current.SeekbarEnabled;
        _alwaysDisplay = SettingsManager.Current.MediaFlyoutAlwaysDisplay;
    }

    private async void MediaIdButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SettingsManager.Current.PlayerInfoEnabled || SettingsManager.Current.CompactLayout) return;
        e.Handled = true;
        _ = TryOpenMediaPlayerAsync();
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TryTogglePlayPauseAsync();
    }

    private async void Forward_Click(object sender, RoutedEventArgs e)
    {
        TrySkipNextAsync();
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
            _isCleaningUp = true;
            _displayRefreshTimer.Stop();
            _displayRefreshTimer.Tick -= DisplayRefreshTimer_Tick;

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

    private void ScheduleDisplayEnvironmentRefresh(string reason)
    {
        if (_isCleaningUp)
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ScheduleDisplayEnvironmentRefresh(reason));
            return;
        }

        _pendingDisplayRefreshReason = reason;
        _displayRefreshTimer.Stop();
        _displayRefreshTimer.Start();
        Logger.Debug($"Scheduled display environment refresh: {reason}");
    }

    private void DisplayRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _displayRefreshTimer.Stop();

        if (_displayRefreshInProgress || _isCleaningUp)
            return;

        _displayRefreshInProgress = true;
        try
        {
            RefreshDisplayEnvironment(_pendingDisplayRefreshReason);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to refresh windows after a display environment change");
        }
        finally
        {
            _displayRefreshInProgress = false;
        }
    }

    private void RefreshDisplayEnvironment(string reason)
    {
        var monitors = MonitorUtil.GetMonitors();
        if (monitors.Count == 0)
        {
            Logger.Warn($"Display environment refresh skipped because no monitors were found ({reason})");
            return;
        }

        Logger.Info($"Refreshing window DPI and placement after display change ({reason}); monitors={monitors.Count}");

        // Stop placement animations that were calculated for the previous work area.
        cts.Cancel();
        _isHiding = true;
        Hide();

        // Move the reusable main flyout to its current target monitor while hidden. This
        // makes WPF process the per-monitor DPI transition before the next animation.
        var targetMonitor = getSelectedMonitor();
        WindowHelper.SetPosition(this, targetMonitor.workArea.Left, targetMonitor.workArea.Top);
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
        UpdateLayout();
        SyncMainFlyoutSizeToCurrentDpi(new WindowInteropHelper(this).Handle);

        // These windows retain an HWND for their lifetime. Recreate them so they cannot
        // keep DPI, work-area, taskbar-parent, or UI Automation state from the old topology.
        if (lockWindow != null)
        {
            lockWindow.Close();
            lockWindow = null;
        }

        if (nextUpWindow != null)
        {
            nextUpWindow.Close();
            nextUpWindow = null;
        }

        if (volumeMixerWindow != null)
        {
            volumeMixerWindow.Close();
            volumeMixerWindow = new VolumeMixerWindow();
        }

        RecreateTaskbarWindow();
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

                        // Explorer recreates the native volume OSD window, so a
                        // previously hidden OSD comes back until re-hidden.
                        if (SettingsManager.Current.VolumeControlEnabled)
                        {
                            Logger.Info("Re-hiding native volume OSD after Explorer restart");
                            VolumeMixerWindow.RehideVolumeOsdAfterExplorerRestart();
                        }
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
        else if (msg == WM_DISPLAYCHANGE)
        {
            ScheduleDisplayEnvironmentRefresh("WM_DISPLAYCHANGE");
            return 0;
        }
        else if (msg == WM_DPICHANGED)
        {
            // Leave the message unhandled so WPF can update its internal DPI state,
            // then size the native HWND from the logical WPF dimensions exactly once.
            Dispatcher.BeginInvoke(() =>
            {
                InvalidateMeasure();
                InvalidateArrange();
                InvalidateVisual();
                UpdateLayout();
                SyncMainFlyoutSizeToCurrentDpi(hwnd);
            }, DispatcherPriority.Loaded);
            return 0;
        }
        else if (msg == WM_SETTINGCHANGE) // system settings changed
        {
            if (wParam.ToInt64() == SPI_SETWORKAREA)
            {
                ScheduleDisplayEnvironmentRefresh("WM_SETTINGCHANGE/SPI_SETWORKAREA");
                return 0;
            }

            string? changedSetting = lParam == IntPtr.Zero ? null : Marshal.PtrToStringUni(lParam);
            if (changedSetting == "SystemDockMode")
            {
                ScheduleDisplayEnvironmentRefresh($"WM_SETTINGCHANGE/{changedSetting}");
                return 0;
            }

            // check if the changed setting is related to theme or accent color
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

    private void SyncMainFlyoutSizeToCurrentDpi(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
            return;

        double dpiScale = dpi / 96.0;
        int pixelWidth = (int)Math.Ceiling(Width * dpiScale);
        int pixelHeight = (int)Math.Ceiling(Height * dpiScale);

        if (!SetWindowPos(
            hwnd,
            0,
            0,
            0,
            pixelWidth,
            pixelHeight,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE))
        {
            Logger.Warn($"Failed to resize MainWindow for DPI; HWND=0x{hwnd.ToInt64():X}, DPI={dpi}, Size={pixelWidth}x{pixelHeight}, Win32Error={Marshal.GetLastWin32Error()}");
        }
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
        volumeMixerWindow = new VolumeMixerWindow();
        taskbarWindow = new TaskbarWindow();
        UpdateTaskbar();
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
                try
                {
                    if (
                        session.Id != currentMediaSession.Id &&
                        session.ControlSession?.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    )
                    {
                        return session.ControlSession.TryPauseAsync().AsTask();
                    }
                }
                catch (Exception ex)
                {
                    // Fire-and-forget fan-out: one dead session must not fail
                    // the whole batch (or go unobserved).
                    Logger.Debug(ex, "Failed to auto-pause session {0}", session.Id);
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