using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static WindowsMediaController.MediaManager;
using WindowsMediaController;
using Windows.Media.Control;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using System.Drawing;


namespace FluentFlyoutWPF
{
    public partial class MainWindow : Window
    {
        //private HotKeyManager hotKeyManager;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_APPCOMMAND = 0x0319;

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _hookProc;

        private CancellationTokenSource cts;

        private static readonly MediaManager mediaManager = new MediaManager();
        private static MediaSession? currentSession = null;

        public MainWindow()
        {
            InitializeComponent();
            this.Visibility = Visibility.Hidden;

            cts = new CancellationTokenSource();

            mediaManager.Start();

            _hookProc = HookCallback;
            _hookId = SetHook(_hookProc);

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Width/2 - Width/2;
            Top = SystemParameters.WorkArea.Height - Height - 80;
        }

        private void MediaManager_OnAnyMediaPropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
        {
            throw new NotImplementedException();
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

                if (vkCode == 0xB3) // Play/Pause
                {
                    ShowMediaFlyout();
                }
                else if (vkCode == 0xB0) // Next Track
                {
                    ShowMediaFlyout();
                }
                else if (vkCode == 0xB1) // Previous Track
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

            this.Visibility = Visibility.Visible;
            this.Topmost = true;

            try
            {
                await Task.Delay(5000, token);
                this.Visibility = Visibility.Hidden;
            }
            catch (TaskCanceledException)
            {
                // do nothing
            }
        }

        private void UpdateUI(MediaSession mediaSession)
        {
            //var mediaProperties = mediaSession.ControlSession.GetPlaybackInfo();
            //if (mediaProperties != null)
            //{
            //    if (mediaSession.ControlSession.GetPlaybackInfo().Controls.IsPauseEnabled)
            //        ControlPlayPause.Content = "II";
            //    else
            //        ControlPlayPause.Content = "▶️";
            //    ControlBack.IsEnabled = ControlForward.IsEnabled = mediaProperties.Controls.IsNextEnabled;
            //}

            var songInfo = mediaSession.ControlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
            if (songInfo != null)
            {
                SongTitle.Text = songInfo.Title;
                SongArtist.Text = songInfo.Artist;
                SongImage.Source = Helper.GetThumbnail(songInfo.Thumbnail);
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
    }
}