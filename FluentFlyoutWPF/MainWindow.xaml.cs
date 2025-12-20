using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyout.Windows;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Windows;
using MicaWPF.Controls;
using MicaWPF.Core.Extensions;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Windows.ApplicationModel;
using Windows.Media.Control;
using Windows.Storage.Streams;
using static WindowsMediaController.MediaManager;


namespace FluentFlyoutWPF;

public partial class MainWindow : MicaWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, IntPtr extraInfo);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;

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

    private TaskbarWindow? taskbarWindow;

    public MainWindow()
    {
        Logger.Info("Starting FluentFlyout MainWindow");
        DataContext = this;
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

        SettingsManager settingsManager = new();
        try
        {
            settingsManager.RestoreSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to restore settings: {ex.Message}");
            Logger.Error(ex, "Failed to restore settings");
        }

        if (SettingsManager.Current.Startup == true) // add to startup programs if enabled, needs improvement
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            string executablePath = Environment.ProcessPath;
            key?.SetValue("FluentFlyout", executablePath);
        }

        cts = new CancellationTokenSource();

        mediaManager.Start();

        _hookProc = HookCallback;
        _hookId = SetHook(_hookProc);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -Width - 20; // workaround for window appearing on the screen before the animation starts
        CustomWindowChrome.CaptionHeight = 0; // hide the title bar
        CustomWindowChrome.UseAeroCaptionButtons = false;
        CustomWindowChrome.GlassFrameThickness = new Thickness(0);

        mediaManager.OnAnyMediaPropertyChanged += MediaManager_OnAnyMediaPropertyChanged;
        mediaManager.OnAnyPlaybackStateChanged += CurrentSession_OnPlaybackStateChanged;
        mediaManager.OnAnyTimelinePropertyChanged += MediaManager_OnAnyTimelinePropertyChanged;
        mediaManager.OnAnySessionClosed += MediaManager_OnAnySessionClosed;

        _positionTimer = new Timer(SeekbarUpdateUi, null, Timeout.Infinite, Timeout.Infinite);
        if (_seekBarEnabled && mediaManager.GetFocusedSession() is { } session)
        {
            UpdateSeekbarCurrentDuration(session.ControlSession.GetTimelineProperties().Position);
        }

        // apply other things on new thread
        Dispatcher.Invoke(() =>
        {
            LocalizationManager.ApplyLocalization();
            // show settings to new users
            if (SettingsManager.Current.LastKnownVersion == "")
                SettingsWindow.ShowInstance();

            try // update last known version. gets the version of the app, works only in release mode
            {
                var version = Package.Current.Id.Version;
                SettingsManager.Current.LastKnownVersion = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                SettingsManager.Current.LastKnownVersion = "debug version";
            }
        });
    }

    private void openSettings(object? sender, EventArgs e)
    {
        SettingsWindow.ShowInstance();
    }

    public int getDuration() // get the duration of the animation based on the speed setting
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

    public void OpenAnimation(MicaWindow window, bool alwaysBottom = false)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers.Actions[0] as BeginStoryboard;
        var storyboard = beginStoryboard.Storyboard;

        DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];

        if (alwaysBottom == false)
        {
            _position = SettingsManager.Current.Position;
            if (_position == 0)
            {
                window.Left = 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0) // if off, don't animate (just appear at the bottom)
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 16;
                else
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height + 4; // appear from the bottom of the screen
                moveAnimation.To = SystemParameters.WorkArea.Height - window.Height - 16;
            }
            else if (_position == 1)
            {
                window.Left = SystemParameters.WorkArea.Width / 2 - window.Width / 2;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 80;
                else
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 60;
                moveAnimation.To = SystemParameters.WorkArea.Height - window.Height - 80;
            }
            else if (_position == 2)
            {
                window.Left = SystemParameters.WorkArea.Width - window.Width - 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 16;
                else
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height + 4;
                moveAnimation.To = SystemParameters.WorkArea.Height - window.Height - 16;
            }
            else if (_position == 3)
            {
                window.Left = 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = 16;
                else
                    moveAnimation.From = -4;
                moveAnimation.To = 16;
            }
            else if (_position == 4)
            {
                window.Left = SystemParameters.WorkArea.Width / 2 - window.Width / 2;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = 16;
                else
                    moveAnimation.From = -4;
                moveAnimation.To = 16;
            }
            else if (_position == 5)
            {
                window.Left = SystemParameters.WorkArea.Width - window.Width - 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = 16;
                else
                    moveAnimation.From = -4;
                moveAnimation.To = 16;
            }
        }
        else
        {
            window.Left = SystemParameters.WorkArea.Width / 2 - window.Width / 2;
            if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 16;
            else
                moveAnimation.From = SystemParameters.WorkArea.Height - window.Height + 4;
            moveAnimation.To = SystemParameters.WorkArea.Height - window.Height - 16;
        }

        int msDuration = getDuration();

        DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        if (SettingsManager.Current.FlyoutAnimationSpeed != 0) opacityAnimation.From = 0;
        opacityAnimation.To = 1;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        if (SettingsManager.Current.FlyoutAnimationEasingStyle == 0) moveAnimation.EasingFunction = opacityAnimation.EasingFunction = null;
        else moveAnimation.EasingFunction = opacityAnimation.EasingFunction = getEasingStyle(true);
        moveAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        storyboard.Begin(window);
    }

    public void CloseAnimation(MicaWindow window, bool alwaysBottom = false)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers.Actions[0] as BeginStoryboard;
        var storyboard = beginStoryboard.Storyboard;

        DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];

        if (alwaysBottom == false)
        {
            _position = SettingsManager.Current.Position;
            if (_position == 0 || _position == 2)
            {
                moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed != 0) moveAnimation.To = SystemParameters.WorkArea.Height - window.Height + 4;
            }
            else if (_position == 1)
            {
                moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 80;
                if (SettingsManager.Current.FlyoutAnimationSpeed != 0) moveAnimation.To = SystemParameters.WorkArea.Height - window.Height - 60;
            }
            else if (_position == 3 || _position == 4 || _position == 5)
            {
                moveAnimation.From = 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed != 0) moveAnimation.To = -4;
            }
        }
        else
        {
            moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 16;
            if (SettingsManager.Current.FlyoutAnimationSpeed != 0) moveAnimation.To = SystemParameters.WorkArea.Height - window.Height + 4;
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
        if (!mediaManager.IsStarted || mediaManager.GetFocusedSession() == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }
        var focusedSession = mediaManager.GetFocusedSession();
        var songInfo = focusedSession.ControlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        var playbackInfo = focusedSession.ControlSession.GetPlaybackInfo();
        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, Helper.GetThumbnail(songInfo.Thumbnail), playbackInfo.PlaybackStatus, playbackInfo.Controls);
    }

    private void reportBug(object? sender, EventArgs e)
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
#if DEBUG
        Logger.Debug("Playback state changed: " + mediaSession.Id + " " + mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
#endif     
        pauseOtherMediaSessionsIfNeeded(mediaSession);

        var focusedSession = mediaManager.GetFocusedSession();
        if (focusedSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var songInfo = focusedSession.ControlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, Helper.GetThumbnail(songInfo.Thumbnail), playbackInfo?.PlaybackStatus, playbackInfo?.Controls);

        if (IsVisible)
        {
            UpdateUI(focusedSession);
            HandlePlayBackState(playbackInfo?.PlaybackStatus);
        }
    }

    // for determining whether MediaPropertyChanged has no changes
    private string previousMediaProperty = "";
    private string previousMediaPropertyThumbnail = "";
    private void MediaManager_OnAnyMediaPropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
    {
#if DEBUG
        Logger.Debug("Media property changed: " + mediaProperties.Title + " " + mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
#endif
        if (mediaManager.GetFocusedSession() == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var songInfo = mediaSession.ControlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        var playbackInfo = mediaSession.ControlSession.GetPlaybackInfo();

        string check = songInfo.Title + songInfo.Artist + playbackInfo.PlaybackStatus;
        string checkThumbnail = songInfo.Thumbnail?.GetHashCode().ToString() ?? "Null";
        bool onlyThumbnailChanged = false;
        if (previousMediaProperty == check)
        {
            onlyThumbnailChanged = true;
            if (previousMediaPropertyThumbnail == checkThumbnail)
            return; // prevent multiple calls for the same song info
        }

        previousMediaProperty = check;
        previousMediaPropertyThumbnail = checkThumbnail;

        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, Helper.GetThumbnail(songInfo.Thumbnail), playbackInfo.PlaybackStatus, playbackInfo.Controls);

        pauseOtherMediaSessionsIfNeeded(mediaSession);

        if (SettingsManager.Current.NextUpEnabled && !FullscreenDetector.IsFullscreenApplicationRunning()) // show NextUpWindow if enabled in settings
        {
            void createNewNextUpWindow()
            {
                Dispatcher.Invoke(() =>
                {
                    if (nextUpWindow == null && playbackInfo.Controls.IsPauseEnabled) // double-check within the Dispatcher to prevent race conditions
                    {
                        nextUpWindow = new NextUpWindow(songInfo.Title, songInfo.Artist, Helper.GetThumbnail(songInfo.Thumbnail));
                        currentTitle = songInfo.Title;
                        nextUpWindow.Closed += (s, e) => nextUpWindow = null; // set nextUpWindow to null when closed
                    }
                });
            }

            if (nextUpWindow == null && IsVisible == false && songInfo.Thumbnail != null && currentTitle != songInfo.Title)
            {
                createNewNextUpWindow();
            }
            else if (nextUpWindow != null && !onlyThumbnailChanged)
            {
                Dispatcher.Invoke(() =>
                {
                    nextUpWindow?.Close(); // must be cleared by the Closed event
                });
                createNewNextUpWindow();
            }
            else if (nextUpWindow != null && songInfo.Thumbnail != null)
            {
                Dispatcher.Invoke(() =>
                {
                    nextUpWindow?.UpdateThumbnail(Helper.GetThumbnail(songInfo.Thumbnail));
                });
            }
        }

        if (IsVisible)
        {
            var focusedSession = mediaManager.GetFocusedSession();
            HandlePlayBackState(focusedSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
            UpdateUI(focusedSession);
        }
    }

    private void MediaManager_OnAnyTimelinePropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        _lastSelfUpdateTimestamp = DateTime.Now;

        if (mediaManager.GetFocusedSession() is not { } session) return;

        if (_seekBarEnabled)
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsActive || _isDragging) return;

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
        var focusedSession = mediaManager.GetFocusedSession();

        if (focusedSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
        }
        else
        {
            var songInfo = focusedSession.ControlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
            var playbackInfo = focusedSession.ControlSession.GetPlaybackInfo();
            taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, Helper.GetThumbnail(songInfo.Thumbnail), playbackInfo.PlaybackStatus, playbackInfo.Controls);
        }
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc) // set the keyboard hook
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule)
        {
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_KEYUP))
        {
            int vkCode = Marshal.ReadInt32(lParam);

            if (vkCode == 0xB3 || vkCode == 0xB0 || vkCode == 0xB1 || vkCode == 0xB2 // Play/Pause, next, previous, stop
                || vkCode == 0xAD || vkCode == 0xAE || vkCode == 0xAF) // Mute, Volume Down, Volume Up
            {
                long currentTime = Environment.TickCount64;

                // debounce to prevent hangs with rapid key presses
                if ((currentTime - _lastFlyoutTime) < 500) // 500ms debounce time
                {
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                _lastFlyoutTime = currentTime;

                ShowMediaFlyout();
            }

            if (SettingsManager.Current.LockKeysEnabled && !FullscreenDetector.IsFullscreenApplicationRunning())
            {
                if (vkCode == 0x14) // Caps Lock
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout(FindResource("LockWindow_CapsLock").ToString(), Keyboard.IsKeyToggled(Key.CapsLock));
                }
                else if (vkCode == 0x90) // Num Lock
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout(FindResource("LockWindow_NumLock").ToString(), Keyboard.IsKeyToggled(Key.NumLock));
                }
                else if (vkCode == 0x91) // Scroll Lock
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout(FindResource("LockWindow_ScrollLock").ToString(), Keyboard.IsKeyToggled(Key.Scroll));
                }
                else if (vkCode == 0x2D && SettingsManager.Current.LockKeysInsertEnabled) // Insert
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout("Insert", Keyboard.IsKeyToggled(Key.Insert));
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public async void ShowMediaFlyout()
    {
        if (mediaManager.GetFocusedSession() == null ||
            !SettingsManager.Current.MediaFlyoutEnabled ||
            FullscreenDetector.IsFullscreenApplicationRunning())
            return;
        UpdateUI(mediaManager.GetFocusedSession());
        if (_seekBarEnabled)
            HandlePlayBackState(mediaManager.GetFocusedSession().ControlSession.GetPlaybackInfo().PlaybackStatus);

        if (nextUpWindow != null) // close NextUpWindow if it's open
        {
            nextUpWindow.Close();
            nextUpWindow = null;
        }

        if (_isHiding == true)
        {
            _isHiding = false;
            OpenAnimation(this);
        }
        cts.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        Visibility = Visibility.Visible;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token); // check if mouse is over every 100ms
                if (!IsMouseOver && !SettingsManager.Current.MediaFlyoutAlwaysDisplay)
                {
                    await Task.Delay(SettingsManager.Current.Duration, token);
                    if (!IsMouseOver)
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

        Dispatcher.Invoke(() =>
        {
            UpdateMediaFlyoutCloseButtonVisibility();
            this.EnableBackdrop(); // ensures the backdrop is enabled as sometimes it gets disabled

            if (mediaSession == null)
            {
                SongTitle.Text = "No media playing";
                SongArtist.Text = "";
                SongImage.ImageSource = null;
                SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Stop16;
                ControlPlayPause.IsEnabled = false;
                ControlPlayPause.Opacity = 0.35;
                ControlBack.IsEnabled = ControlForward.IsEnabled = false;
                ControlBack.Opacity = ControlForward.Opacity = 0.35;
                return;
            }

            var mediaProperties = mediaSession.ControlSession.GetPlaybackInfo();
            if (mediaProperties != null)
            {
                if (mediaSession.ControlSession.GetPlaybackInfo().Controls.IsPauseEnabled)
                {
                    ControlPlayPause.IsEnabled = true;
                    ControlPlayPause.Opacity = 1;
                    SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause16;
                }
                else
                {
                    ControlPlayPause.IsEnabled = true;
                    ControlPlayPause.Opacity = 1;
                    SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Play16;
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
                    (string title, ImageSource? Icon) = MediaPlayerData.getMediaPlayerData(mediaSession.Id);
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

                // acrylic effect setting
                if (SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled != _acrylicEnabled
                || SettingsManager.Current.AppTheme != _themeOption) // if theme changes, reapply acrylic for updated background color
                {
                    _acrylicEnabled = SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled;
                    ToggleBlur(); // called enabled but it actually toggles based on the setting
                }
            }

            var songInfo = mediaSession.ControlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
            if (songInfo != null)
            {
                SongTitle.Text = songInfo.Title;
                SongArtist.Text = songInfo.Artist;
                var image = Helper.GetThumbnail(songInfo.Thumbnail);
                SongImage.ImageSource = image;

                // background blurred image
                if (SettingsManager.Current.MediaFlyoutBackgroundBlur != 0)
                {
                    // make image 1:1 aspect ratio so gradient masks work for non-square images
                    var croppedImage = Helper.CropToSquare(image);

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
                    var timeline = mediaSession.ControlSession.GetTimelineProperties();

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
            extraWidth += SettingsManager.Current.PlayerInfoEnabled ? 72 : 72; // disabled player info should temporarily keep the widget the same width as no one seems to like the small version

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
        if (mediaManager.GetFocusedSession() == null)
            return;

        await mediaManager.GetFocusedSession().ControlSession.TrySkipPreviousAsync();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        keybd_event(0xB3, 0, 0, IntPtr.Zero);

        if (mediaManager.GetFocusedSession() == null)
            return;

        //var controlsInfo = mediaManager.GetFocusedSession().ControlSession.GetPlaybackInfo().Controls;

        //if (controlsInfo.IsPauseEnabled == true)
        //{
        //    await mediaManager.GetFocusedSession().ControlSession.TryPauseAsync();
        //}
        //else if (controlsInfo.IsPlayEnabled == true)
        //    await mediaManager.GetFocusedSession().ControlSession.TryPlayAsync();
        if (mediaManager.GetFocusedSession().ControlSession.GetPlaybackInfo().Controls.IsPauseEnabled)
            SymbolPlayPause.Dispatcher.Invoke(() => SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause16);
        else
            SymbolPlayPause.Dispatcher.Invoke(() => SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Play16);
    }

    private async void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (mediaManager.GetFocusedSession() == null)
            return;

        await mediaManager.GetFocusedSession().ControlSession.TrySkipNextAsync();
    }

    private async void Repeat_Click(object sender, RoutedEventArgs e)
    {
        if (mediaManager.GetFocusedSession() == null)
            return;

        if (mediaManager.GetFocusedSession().ControlSession.GetPlaybackInfo().AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.None)
        {
            SymbolRepeat.Dispatcher.Invoke(() => SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAll24);
            await mediaManager.GetFocusedSession().ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.List);
        }
        else if (mediaManager.GetFocusedSession().ControlSession.GetPlaybackInfo().AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.List)
        {
            SymbolRepeat.Dispatcher.Invoke(() => SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeat124);
            await mediaManager.GetFocusedSession().ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.Track);
        }
        else if (mediaManager.GetFocusedSession().ControlSession.GetPlaybackInfo().AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.Track)
        {
            SymbolRepeat.Dispatcher.Invoke(() => SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAllOff24);
            await mediaManager.GetFocusedSession().ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.None);
        }
    }

    private async void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        if (mediaManager.GetFocusedSession() == null)
            return;

        if (mediaManager.GetFocusedSession().ControlSession.GetPlaybackInfo().IsShuffleActive == true)
        {
            SymbolShuffle.Dispatcher.Invoke(() => SymbolShuffle.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24);
            await mediaManager.GetFocusedSession().ControlSession.TryChangeShuffleActiveAsync(false);
        }
        else
        {
            SymbolShuffle.Dispatcher.Invoke(() => SymbolShuffle.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24);
            await mediaManager.GetFocusedSession().ControlSession.TryChangeShuffleActiveAsync(true);
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
        if (mediaManager.GetFocusedSession() is { } session)
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
        if (mediaManager.GetFocusedSession() is not { } session) return;

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

            // unhook keyboard hook
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            // clean up other resources
            if (lockWindow?.IsLoaded == true)
                lockWindow.Close();

            if (nextUpWindow?.IsLoaded == true)
                nextUpWindow.Close();

            if (taskbarWindow?.IsLoaded == true)
                taskbarWindow.Close();

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

    internal static class Helper
    {
        private const int MaxThumbnailSize = 512;

        internal static BitmapImage? GetThumbnail(IRandomAccessStreamReference Thumbnail, bool convertToPng = true)
        {
            if (Thumbnail == null)
                return null;

            BitmapImage image = new();
            using (var imageStream = Thumbnail.OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead())
            {
                // initialize the BitmapImage
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = MaxThumbnailSize;
                image.StreamSource = imageStream;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }

        internal static CroppedBitmap? CropToSquare(BitmapImage? sourceImage)
        {
            if (sourceImage == null)
                return null;

            int size = (int)Math.Min(sourceImage.PixelWidth, sourceImage.PixelHeight);
            int x = (sourceImage.PixelWidth - size) / 2;
            int y = (sourceImage.PixelHeight - size) / 2;

            var rect = new Int32Rect(x, y, size, size);

            // create a CroppedBitmap (this is a lightweight object)
            var croppedBitmap = new CroppedBitmap(sourceImage, rect);

            croppedBitmap.Freeze();
            return croppedBitmap;
        }
    }


    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);


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

    private async void MicaWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Hide();
        UpdateUILayout();
        ThemeManager.ApplySavedTheme();

        // add tray icon if not hidden
        try
        {
            if (!SettingsManager.Current.NIconHide)
            {
                nIcon.Visibility = Visibility.Visible;
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
        CloseAnimation(this);
        _isHiding = true;
        Task.Run(async () =>
        {
            await Task.Delay(getDuration());
            Dispatcher.Invoke(() =>
            {
                if (_isHiding)
                {
                    Hide();
                    if (_seekBarEnabled)
                        HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
                }
            });
        });
    }
}
