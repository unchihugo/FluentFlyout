using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using FluentFlyoutWPF.Classes;
using FluentFlyout.Properties;
using FluentFlyoutWPF.Windows;
using MicaWPF.Controls;
using MicaWPF.Core.Extensions;
using Microsoft.Win32;
using static WindowsMediaController.MediaManager;


namespace FluentFlyoutWPF;

public partial class MainWindow : MicaWindow
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, IntPtr extraInfo);
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_APPCOMMAND = 0x0319;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc _hookProc;

    private CancellationTokenSource cts; // to close the flyout after a certain time

    private static readonly WindowsMediaController.MediaManager mediaManager = new();
    private static MediaSession? currentSession = null;

    // for detecting changes in settings (lazy way)
    private int _position = Settings.Default.Position;
    private bool _layout = Settings.Default.CompactLayout;
    private bool _repeatEnabled = Settings.Default.RepeatEnabled;
    private bool _shuffleEnabled = Settings.Default.ShuffleEnabled;
    private bool _playerInfoEnabled = Settings.Default.PlayerInfoEnabled;
    private bool _centerTitleArtist = Settings.Default.CenterTitleArtist;

    static Mutex singleton = new Mutex(true, "FluentFlyout"); // to prevent multiple instances of the app
    private NextUpWindow? nextUpWindow = null; // to prevent multiple instances of NextUpWindow
    private string currentTitle = ""; // to prevent NextUpWindow from showing the same song

    private LockWindow? lockWindow;

    public MainWindow()
    {
        WindowHelper.SetNoActivate(this); // prevents some fullscreen apps from minimizing
        InitializeComponent();
        WindowHelper.SetTopmost(this); // more prevention of fullscreen apps minimizing

        if (!singleton.WaitOne(TimeSpan.Zero, true)) // if another instance is already running, close this one
        {
            Application.Current.Shutdown();
        }


        if (Settings.Default.Startup == true) // add to startup programs if enabled, needs improvement
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
    }

    private void openSettings(object? sender, EventArgs e)
    {
        SettingsWindow.ShowInstance();
    }

    public int getDuration() // get the duration of the animation based on the speed setting
    {
        int msDuration = Settings.Default.FlyoutAnimationSpeed switch
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
        EasingFunctionBase easingStyle = Settings.Default.FlyoutAnimationEasingStyle switch
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
            _position = Settings.Default.Position;
            if (_position == 0)
            {
                window.Left = 16;
                if (Settings.Default.FlyoutAnimationSpeed == 0) // if off, don't animate (just appear at the bottom)
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 16;
                else
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height + 4; // appear from the bottom of the screen
                moveAnimation.To = SystemParameters.WorkArea.Height - window.Height - 16;
            }
            else if (_position == 1)
            {
                window.Left = SystemParameters.WorkArea.Width / 2 - window.Width / 2;
                if (Settings.Default.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 80;
                else
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 60;
                moveAnimation.To = SystemParameters.WorkArea.Height - window.Height - 80;
            }
            else if (_position == 2)
            {
                window.Left = SystemParameters.WorkArea.Width - window.Width - 16;
                if (Settings.Default.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 16;
                else
                    moveAnimation.From = SystemParameters.WorkArea.Height - window.Height + 4;
                moveAnimation.To = SystemParameters.WorkArea.Height - window.Height - 16;
            }
            else if (_position == 3)
            {
                window.Left = 16;
                if (Settings.Default.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = 16;
                else
                    moveAnimation.From = -4;
                moveAnimation.To = 16;
            }
            else if (_position == 4)
            {
                window.Left = SystemParameters.WorkArea.Width / 2 - window.Width / 2;
                if (Settings.Default.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = 16;
                else
                    moveAnimation.From = -4;
                moveAnimation.To = 16;
            }
            else if (_position == 5)
            {
                window.Left = SystemParameters.WorkArea.Width - window.Width - 16;
                if (Settings.Default.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = 16;
                else
                    moveAnimation.From = -4;
                moveAnimation.To = 16;
            }
        }
        else
        {
            window.Left = SystemParameters.WorkArea.Width / 2 - window.Width / 2;
            if (Settings.Default.FlyoutAnimationSpeed == 0)
                moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 16;
            else
                moveAnimation.From = SystemParameters.WorkArea.Height - window.Height + 4;
            moveAnimation.To = SystemParameters.WorkArea.Height - window.Height - 16;
        }

        int msDuration = getDuration();

        DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        if (Settings.Default.FlyoutAnimationSpeed != 0) opacityAnimation.From = 0;
        opacityAnimation.To = 1;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        if (Settings.Default.FlyoutAnimationEasingStyle == 0) moveAnimation.EasingFunction = opacityAnimation.EasingFunction = null;
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
            _position = Settings.Default.Position;
            if (_position == 0 || _position == 2)
            {
                moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 16;
                if (Settings.Default.FlyoutAnimationSpeed != 0) moveAnimation.To = SystemParameters.WorkArea.Height - window.Height + 4;
            }
            else if (_position == 1)
            {
                moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 80;
                if (Settings.Default.FlyoutAnimationSpeed != 0) moveAnimation.To = SystemParameters.WorkArea.Height - window.Height - 60;
            }
            else if (_position == 3 || _position == 4 || _position == 5)
            {
                moveAnimation.From = 16;
                if (Settings.Default.FlyoutAnimationSpeed != 0) moveAnimation.To = -4;
            }
        }
        else
        {
            moveAnimation.From = SystemParameters.WorkArea.Height - window.Height - 16;
            if (Settings.Default.FlyoutAnimationSpeed != 0) moveAnimation.To = SystemParameters.WorkArea.Height - window.Height + 4;
        }

        int msDuration = getDuration();

        DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        opacityAnimation.From = 1;
        if (Settings.Default.FlyoutAnimationSpeed != 0) opacityAnimation.To = 0;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        if (Settings.Default.FlyoutAnimationEasingStyle == 0) moveAnimation.EasingFunction = opacityAnimation.EasingFunction = null;
        else moveAnimation.EasingFunction = opacityAnimation.EasingFunction = getEasingStyle(false);
        moveAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        storyboard.Begin(window);
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

    private void CurrentSession_OnPlaybackStateChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null)
    {
        UpdateUI(mediaManager.GetFocusedSession());
    }

    private void MediaManager_OnAnyMediaPropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
    {
        if (mediaManager.GetFocusedSession() == null) return;
        if (Settings.Default.NextUpEnabled || !FullscreenDetector.IsFullscreenApplicationRunning()) // show NextUpWindow if enabled in settings
        {
            var songInfo = mediaSession.ControlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
            if (nextUpWindow == null && IsVisible == false && songInfo.Thumbnail != null && currentTitle != songInfo.Title)
            {
                Dispatcher.Invoke(() =>
                {
                    if (nextUpWindow == null && mediaSession.ControlSession.GetPlaybackInfo().Controls.IsPauseEnabled) // double-check within the Dispatcher to prevent race conditions
                    {
                        nextUpWindow = new NextUpWindow(songInfo.Title, songInfo.Artist, Helper.GetThumbnail(songInfo.Thumbnail));
                        currentTitle = songInfo.Title;
                        nextUpWindow.Closed += (s, e) => nextUpWindow = null; // set nextUpWindow to null when closed
                    }
                });
            }
        }

        UpdateUI(mediaManager.GetFocusedSession());
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
                ShowMediaFlyout();
            }

            if (Settings.Default.LockKeysEnabled || !FullscreenDetector.IsFullscreenApplicationRunning())
            {
                if (vkCode == 0x14) // Caps Lock
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout("Caps Lock", Keyboard.IsKeyToggled(Key.CapsLock));
                }

                if (vkCode == 0x90) // Num Lock
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout("Num Lock", Keyboard.IsKeyToggled(Key.NumLock));
                }
                if (vkCode == 0x91) // Scroll Lock
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout("Scroll Lock", Keyboard.IsKeyToggled(Key.Scroll));
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private async void ShowMediaFlyout()
    {
        if (mediaManager.GetFocusedSession() == null ||
            !Settings.Default.MediaFlyoutEnabled ||
            FullscreenDetector.IsFullscreenApplicationRunning())
            return;
        UpdateUI(mediaManager.GetFocusedSession());

        if (nextUpWindow != null) // close NextUpWindow if it's open
        {
            nextUpWindow.Close();
            nextUpWindow = null;
        }

        if (Visibility == Visibility.Hidden)
        {
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
                if (!IsMouseOver)
                {
                    await Task.Delay(Settings.Default.Duration, token);
                    if (!IsMouseOver)
                    {
                        CloseAnimation(this);
                        await Task.Delay(getDuration());
                        Hide();
                        break;
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            // do nothing
        }
    }

    private void UpdateUI(MediaSession mediaSession)
    {
        if (_layout != Settings.Default.CompactLayout ||
            _shuffleEnabled != Settings.Default.ShuffleEnabled ||
            _repeatEnabled != Settings.Default.ShuffleEnabled ||
            _playerInfoEnabled != Settings.Default.PlayerInfoEnabled ||
            _centerTitleArtist != Settings.Default.CenterTitleArtist)
            UpdateUILayout();

        Dispatcher.Invoke(() =>
        {
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

                if (Settings.Default.RepeatEnabled && !Settings.Default.CompactLayout)
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


                if (Settings.Default.ShuffleEnabled && !Settings.Default.CompactLayout)
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


                if (Settings.Default.PlayerInfoEnabled && !Settings.Default.CompactLayout)
                {
                    MediaIdStackPanel.Visibility = Visibility.Visible;
                    MediaId.Text = mediaSession.Id;
                }
                else MediaIdStackPanel.Visibility = Visibility.Collapsed;
            }

            var songInfo = mediaSession.ControlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
            if (songInfo != null)
            {
                SongTitle.Text = songInfo.Title;
                SongArtist.Text = songInfo.Artist;
                SongImage.ImageSource = Helper.GetThumbnail(songInfo.Thumbnail);
                if (SongImage.ImageSource == null) SongImagePlaceholder.Visibility = Visibility.Visible;
                else SongImagePlaceholder.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void UpdateUILayout() // update the layout based on the settings
    {
        Dispatcher.Invoke(() =>
        {
            int extraWidth = Settings.Default.RepeatEnabled ? 36 : 0;
            extraWidth += Settings.Default.ShuffleEnabled ? 36 : 0;
            extraWidth += Settings.Default.PlayerInfoEnabled ? 72 : 0;
            if (Settings.Default.CompactLayout) // compact layout
            {
                Height = 60;
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
            }
            else // normal layout
            {
                Height = 116;
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
        });

        if (Settings.Default.CenterTitleArtist)
        {
            SongTitle.HorizontalAlignment = HorizontalAlignment.Center;
            SongArtist.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else
        {
            SongTitle.HorizontalAlignment = HorizontalAlignment.Left;
            SongArtist.HorizontalAlignment = HorizontalAlignment.Left;
        }

        _layout = Settings.Default.CompactLayout;
        _repeatEnabled = Settings.Default.RepeatEnabled;
        _shuffleEnabled = Settings.Default.ShuffleEnabled;
        _playerInfoEnabled = Settings.Default.PlayerInfoEnabled;
        _centerTitleArtist = Settings.Default.CenterTitleArtist;
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

    protected override void OnClosed(EventArgs e)
    {
        UnhookWindowsHookEx(_hookId);
        base.OnClosed(e);
    }

    internal static class Helper
    {
        internal static BitmapImage? GetThumbnail(IRandomAccessStreamReference Thumbnail, bool convertToPng = true)
        {
            if (Thumbnail == null)
                return null;

            var thumbnailStream = Thumbnail.OpenReadAsync().GetAwaiter().GetResult();
            byte[] thumbnailBytes = new byte[thumbnailStream.Size];
            using (DataReader reader = new DataReader(thumbnailStream))
            {
                reader.LoadAsync((uint)thumbnailStream.Size).GetAwaiter().GetResult();
                reader.ReadBytes(thumbnailBytes);
            }

            byte[] imageBytes = thumbnailBytes;

            if (convertToPng)
            {
                using var fileMemoryStream = new System.IO.MemoryStream(thumbnailBytes);
                Bitmap thumbnailBitmap = (Bitmap)Bitmap.FromStream(fileMemoryStream);

                if (!thumbnailBitmap.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.Png))
                {
                    using var pngMemoryStream = new System.IO.MemoryStream();
                    thumbnailBitmap.Save(pngMemoryStream, System.Drawing.Imaging.ImageFormat.Png);
                    imageBytes = pngMemoryStream.ToArray();
                }
            }

            var image = new BitmapImage();
            using (var ms = new System.IO.MemoryStream(imageBytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
            }

            return image;
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


    private void MicaWindow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) // keep the flyout open when mouse is over
    {
        ShowMediaFlyout();
    }

    private void NotifyIconQuit_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void MicaWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Hide();
        UpdateUILayout();
        ThemeManager.ApplySavedTheme();
    }

    private void nIcon_LeftClick(Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e) // change the behavior of the tray icon
    {
        if (Settings.Default.nIconLeftClick == 0)
        {
            openSettings(sender, e);
            //Wpf.Ui.Appearance.ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica); // to change the theme
            //ThemeService themeService = new ThemeService();
            //themeService.ChangeTheme(MicaWPF.Core.Enums.WindowsTheme.Light);
        }
        else if (Settings.Default.nIconLeftClick == 1) ShowMediaFlyout();
    }
}