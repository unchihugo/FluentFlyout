using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Wpf.Ui.Markup;

namespace FluentFlyout.Windows;

/// <summary>
/// Interaction logic for TaskbarWindow.xaml
/// </summary>
public partial class TaskbarWindow : Window
{
    // --- Win32 APIs ---
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);

    // SetWindowPos Flags
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_ASYNCWINDOWPOS = 0x4000;
    // ------------------

    private DispatcherTimer _timer;

    private SolidColorBrush _hitTestTransparent;

    // Cached width calculations
    private string _cachedTitleText = string.Empty;
    private string _cachedArtistText = string.Empty;
    private double _cachedTitleWidth = 0;
    private double _cachedArtistWidth = 0;

    public TaskbarWindow()
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);

        _hitTestTransparent = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(150);
        _timer.Tick += (s, e) => UpdatePosition();
        _timer.Start();

        Show();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
    }

    //private void Grid_MouseEnter(object sender, MouseEventArgs e)
    //{
    //    // hover effects
    //    var brush = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    //    MainBorder.Background = new SolidColorBrush(brush.Color) { Opacity = 0.075 };
    //    var secondBrush = (SolidColorBrush)Application.Current.Resources["TextFillColorDisabledBrush"];
    //    TopBorder.BorderBrush = new SolidColorBrush(secondBrush.Color) { Opacity = 0.2 };
    //}

    //private void Grid_MouseLeave(object sender, MouseEventArgs e)
    //{
    //    MainBorder.Background = Brushes.Transparent;
    //    TopBorder.BorderBrush = Brushes.Transparent;
    //}

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        // hover effects with animations
        var brush = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var targetBackgroundBrush = new SolidColorBrush(brush.Color) { Opacity = 0.075 };

        var secondBrush = (SolidColorBrush)Application.Current.Resources["TextFillColorDisabledBrush"];
        TopBorder.BorderBrush = new SolidColorBrush(secondBrush.Color) { Opacity = 0.25 };

        // Animate background
        var backgroundAnimation = new ColorAnimation
        {
            To = targetBackgroundBrush.Color,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = 0.075,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        // Animate back to transparent
        var backgroundAnimation = new ColorAnimation
        {
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        MainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);

        TopBorder.BorderBrush = Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // flyout main flyout when clicked
        var mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow.ShowMediaFlyout();
    }

    private void SetupWindow()
    {
        var interop = new WindowInteropHelper(this);
        IntPtr myHandle = interop.Handle;

        Background = _hitTestTransparent; // ensures that non-content areas also trigger MouseEnter event

        // 3. Find the Taskbar and ReBar
        IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);
        IntPtr rebarHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "ReBarWindow32", null);

        if (taskbarHandle != IntPtr.Zero)
        {
            // 1. Modify Style: Remove Popup, Add Child
            // This prevents the window from trying to float above the taskbar as a separate entity
            int style = GetWindowLong(myHandle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD;
            SetWindowLong(myHandle, GWL_STYLE, style);

            // 2. Set Parent
            SetParent(myHandle, taskbarHandle);

            // 3. Initial Calculation
            CalculateAndSetPosition(taskbarHandle, myHandle);
        }

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }
    }

    private void UpdatePosition()
    {
        // Check premium status before allowing widget to be displayed
        if (!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked) 
            return;

        var interop = new WindowInteropHelper(this);
        IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);

        if (taskbarHandle != IntPtr.Zero && interop.Handle != IntPtr.Zero)
        {
            CalculateAndSetPosition(taskbarHandle, interop.Handle);
        }
    }

    private void CalculateAndSetPosition(IntPtr taskbarHandle, IntPtr myHandle)
    {
        // get DPI scaling
        PresentationSource source = PresentationSource.FromVisual(this);
        double dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
        double dpiScaleY = source.CompositionTarget.TransformToDevice.M22;

        // calculate widget width - use cached values if text hasn't changed
        var scale = 0.9;
        string currentTitle = SongTitle.Text;
        string currentArtist = SongArtist.Text;
        
        if (!string.Equals(currentTitle, _cachedTitleText, StringComparison.Ordinal))
        {
            _cachedTitleWidth = StringWidth.GetStringWidth(currentTitle);
            _cachedTitleText = currentTitle;
        }
        if (!string.Equals(currentArtist, _cachedArtistText, StringComparison.Ordinal))
        {
            _cachedArtistWidth = StringWidth.GetStringWidth(currentArtist);
            _cachedArtistText = currentArtist;
        }
        
        double logicalWidth = Math.Max(_cachedTitleWidth, _cachedArtistWidth) + 40 * scale; // add margin for cover image
        // maximum width limit, matches default media flyout width
        logicalWidth = Math.Min(logicalWidth, 310);

            SongTitle.Width = logicalWidth - 40 * scale;
            SongArtist.Width = logicalWidth - 40 * scale;

            int physicalWidth = (int)(logicalWidth * dpiScaleX);
        int physicalHeight = (int)(this.Height * dpiScaleY);

        // Get Taskbar dimensions
        RECT taskbarRect;
        GetWindowRect(taskbarHandle, out taskbarRect);
        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;

        // Centered vertically
        int physicalTop = (taskbarHeight - physicalHeight) / 2;
        int nativeWidgetsPadding = 216;

        int physicalLeft = 20; // maybe add automatic widget padding?
        if (SettingsManager.Current.TaskbarWidgetPadding)
        {
            physicalLeft += nativeWidgetsPadding;
        }

            // Apply using SetWindowPos (Bypassing WPF layout engine)
            SetWindowPos(myHandle, IntPtr.Zero,
                     physicalLeft, physicalTop,
                     physicalWidth, physicalHeight,
                     SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_SHOWWINDOW);
    }

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus)
    {
        // Check premium status - hide widget if not unlocked
        if (!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked)
        {
            Visibility = Visibility.Collapsed;
            return; 
        }

        if (title == "-" && artist == "-")
        {
            // no media playing, hide UI
            Dispatcher.Invoke(() =>
            {
                SongTitle.Text = "";
                SongArtist.Text = "";
                SongInfoStackPanel.Visibility = Visibility.Collapsed;
                SongImagePlaceholder.Symbol = Wpf.Ui.Controls.SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                UpdatePosition();
                Visibility = Visibility.Visible;
            });
            return;
        }

        bool isPaused = false;
        if (playbackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            isPaused = true;
        }

        Dispatcher.Invoke(() =>
        {
            if (!String.IsNullOrEmpty(title))
            {
                SongTitle.Text = title;
            }
            else
            {
                SongTitle.Text = "-";
            }

            if (!String.IsNullOrEmpty(artist))
            {
                SongArtist.Text = artist;
            }
            else
            {
                SongArtist.Text = "-";
            }

            if (icon != null)
            {
                if (isPaused)
                { // show pause icon overlay
                    SongImagePlaceholder.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause24;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.Opacity = 0.4;
                }
                else
                {
                    SongImagePlaceholder.Visibility = Visibility.Collapsed;
                    SongImage.Opacity = 1;
                }
                SongImage.ImageSource = icon;
            }
            else
            {
                SongImagePlaceholder.Symbol = Wpf.Ui.Controls.SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
            }

            SongTitle.Visibility = Visibility.Visible;
            SongArtist.Visibility = Visibility.Visible;
            SongInfoStackPanel.Visibility = Visibility.Visible;
            Visibility = Visibility.Visible;
        });
    }
}