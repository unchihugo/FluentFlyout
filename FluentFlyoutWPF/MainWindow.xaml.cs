using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static WindowsMediaController.MediaManager;
using WindowsMediaController;
using Windows.Media.Control;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using MicaWPF.Controls;
using Forms = System.Windows.Forms;
using System.IO;
using System.Windows.Media.Animation;
using FluentFlyout;
using FluentFlyout.Properties;


namespace FluentFlyoutWPF
{
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

        private CancellationTokenSource cts;

        private static readonly MediaManager mediaManager = new MediaManager();
        private static MediaSession? currentSession = null;

        Forms.NotifyIcon notifyIcon = new NotifyIcon();
        private int _position = Settings.Default.Position;

        public MainWindow()
        {
            InitializeComponent();
            Visibility = Visibility.Hidden;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "FluentFlyout2.ico");
            notifyIcon.Icon = new Icon(path);
            notifyIcon.Text = "FluentFlyout";
            notifyIcon.DoubleClick += openSettings;
            notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            notifyIcon.ContextMenuStrip.Items.Add("Settings", null, openSettings);
            notifyIcon.ContextMenuStrip.Items.Add("Repository", null, openRepository);
            notifyIcon.ContextMenuStrip.Items.Add("Report bug", null, reportBug);
            notifyIcon.ContextMenuStrip.Items.Add("Quit FluentFlyout", null, (s, e) => System.Windows.Application.Current.Shutdown());
            notifyIcon.Visible = true;
            //notifyIcon.ShowBalloonTip(5000, "Moved to tray", "The media flyout is running in the background", ToolTipIcon.Info);

            if (Settings.Default.Startup)
            {
                Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                key.SetValue("FluentFlyout", Forms.Application.ExecutablePath);
            }

            cts = new CancellationTokenSource();

            mediaManager.Start();

            _hookProc = HookCallback;
            _hookId = SetHook(_hookProc);

            WindowStartupLocation = WindowStartupLocation.Manual;

            mediaManager.OnAnyMediaPropertyChanged += MediaManager_OnAnyMediaPropertyChanged;
            mediaManager.OnAnyPlaybackStateChanged += CurrentSession_OnPlaybackStateChanged;
        }

        private void openSettings(object? sender, EventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            if (settingsWindow.ShowDialog() == true)
            {
                //_position = settingsWindow.Position;
            }
        }

        private void OpenAnimation()
        {

            var eventTriggers = Triggers[0] as EventTrigger;
            var beginStoryboard = eventTriggers.Actions[0] as BeginStoryboard;
            var storyboard = beginStoryboard.Storyboard;

            DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];
            _position = Settings.Default.Position;
            if (_position == 0)
            {
                Left = 16;
                moveAnimation.From = SystemParameters.WorkArea.Height - Height - 0;
                moveAnimation.To = SystemParameters.WorkArea.Height - Height - 16;
            }
            else if (_position == 1)
            {
                Left = SystemParameters.WorkArea.Width / 2 - Width / 2;
                moveAnimation.From = SystemParameters.WorkArea.Height - Height - 60;
                moveAnimation.To = SystemParameters.WorkArea.Height - Height - 80;
            }
            else if (_position == 2)
            {
                Left = SystemParameters.WorkArea.Width - Width - 16;
                moveAnimation.From = SystemParameters.WorkArea.Height - Height - 0;
                moveAnimation.To = SystemParameters.WorkArea.Height - Height - 16;
            }
            else if (_position == 3)
            {
                Left = 16;
                moveAnimation.From = 0;
                moveAnimation.To = 16;
            }
            else if (_position == 4)
            {
                Left = SystemParameters.WorkArea.Width / 2 - Width / 2;
                moveAnimation.From = 0;
                moveAnimation.To = 16;
            }
            else if (_position == 5)
            {
                Left = SystemParameters.WorkArea.Width - Width - 16;
                moveAnimation.From = 0;
                moveAnimation.To = 16;
            }

            DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
            opacityAnimation.From = 0;
            opacityAnimation.To = 1;

            EasingFunctionBase easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            moveAnimation.EasingFunction = opacityAnimation.EasingFunction = easing;

            storyboard.Begin(this);
        }

        private void CloseAnimation()
        {
            var eventTriggers = Triggers[0] as EventTrigger;
            var beginStoryboard = eventTriggers.Actions[0] as BeginStoryboard;
            var storyboard = beginStoryboard.Storyboard;

            DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];
            _position = Settings.Default.Position;
            if (_position == 0 || _position == 2)
            {
                moveAnimation.From = SystemParameters.WorkArea.Height - Height - 16;
                moveAnimation.To = SystemParameters.WorkArea.Height - Height - 0;
            }
            else if (_position == 1)
            {
                moveAnimation.From = SystemParameters.WorkArea.Height - Height - 80;
                moveAnimation.To = SystemParameters.WorkArea.Height - Height - 60;
            }
            else if (_position == 3 || _position == 4 || _position == 5)
            {
                moveAnimation.From = 16;
                moveAnimation.To = 0;
            }

            DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
            opacityAnimation.From = 1;
            opacityAnimation.To = 0;

            EasingFunctionBase easing = new CubicEase { EasingMode = EasingMode.EaseIn };
            moveAnimation.EasingFunction = opacityAnimation.EasingFunction = easing;

            storyboard.Begin(this);
        }

        private void reportBug(object? sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/unchihugo/FluentFlyout/issues/new",
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
            UpdateUI(mediaManager.GetFocusedSession());
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
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

            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private async void ShowMediaFlyout()
        {
            UpdateUI(mediaManager.GetFocusedSession());
            cts.Cancel();
            cts = new CancellationTokenSource();
            var token = cts.Token;

            if (Visibility == Visibility.Hidden) OpenAnimation();
            Visibility = Visibility.Visible;
            Topmost = true;

            try
            {
                await Task.Delay(Settings.Default.Duration, token);
                CloseAnimation();
                await Task.Delay(300);
                Hide();
            }
            catch (TaskCanceledException)
            {
                // do nothing
            }
        }

        private void UpdateUI(MediaSession mediaSession)
        {
            Dispatcher.Invoke(() =>
            {
                if (mediaSession == null)
                {
                    SongTitle.Text = "No media playing";
                    SongArtist.Text = "";
                    SongImage.Source = null;
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
                        SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause16;
                    else
                        SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Play16;
                    ControlBack.IsEnabled = ControlForward.IsEnabled = mediaProperties.Controls.IsNextEnabled;
                    ControlBack.Opacity = ControlForward.Opacity = mediaProperties.Controls.IsNextEnabled ? 1 : 0.35;
                    MediaId.Text = mediaSession.Id;
                }


                var songInfo = mediaSession.ControlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
                if (songInfo != null)
                {
                    SongTitle.Text = songInfo.Title;
                    SongArtist.Text = songInfo.Artist;
                    SongImage.Source = Helper.GetThumbnail(songInfo.Thumbnail);
                }
            });
        }

        private async void Back_Click(object sender, RoutedEventArgs e)
        {
            if (mediaManager.GetFocusedSession() == null)
                return;

            await mediaManager.GetFocusedSession().ControlSession.TrySkipPreviousAsync();
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e)
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


        protected override void OnClosed(EventArgs e)
        {
            UnhookWindowsHookEx(_hookId);
            base.OnClosed(e);
            notifyIcon.Dispose();
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


        private void MicaWindow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ShowMediaFlyout();
        }
    }
}