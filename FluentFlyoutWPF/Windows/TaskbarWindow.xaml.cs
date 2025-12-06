using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

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

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    
    private readonly DispatcherTimer _timer;
    private readonly SolidColorBrush _hitTestTransparent;
    private readonly int _nativeWidgetsPadding = 216;
    private readonly double _scale = 0.9;

    // unused for now
    //private readonly DoubleAnimation fadeIn = new()
    //{
    //    From = 0.0,
    //    To = 0.5,
    //    Duration = new(TimeSpan.FromSeconds(2)),
    //    FillBehavior = FillBehavior.Stop
    //};
    //private readonly DoubleAnimation fadeOut = new()
    //{
    //    From = 0.5,
    //    To = 0.0,
    //    Duration = new(TimeSpan.FromSeconds(2)),
    //    FillBehavior = FillBehavior.Stop
    //};

    // Cached width calculations
    private string _cachedTitleText = string.Empty;
    private string _cachedArtistText = string.Empty;
    private double _cachedTitleWidth = 0;
    private double _cachedArtistWidth = 0;
    private double _dpiScaleX;
    private double _dpiScaleY;
    //private Task _crossFadeTask = Task.CompletedTask;

    public TaskbarWindow()
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);

        _hitTestTransparent = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));

        // initialize here in case we want to restart the window
        _dpiScaleX = 0;
        _dpiScaleY = 0;

        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1000);
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
        if (!SettingsManager.Current.TaskbarWidgetClickable || String.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

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
        if (!SettingsManager.Current.TaskbarWidgetClickable || String.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

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

        TopBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarWidgetClickable) return;

        // flyout main flyout when clicked
        var mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow.ShowMediaFlyout();
    }

    private void SetupWindow()
    {
        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr myHandle = interop.Handle;

            Background = _hitTestTransparent; // ensures that non-content areas also trigger MouseEnter event

            IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);

            if (taskbarHandle != IntPtr.Zero)
            {
                // This prevents the window from trying to float above the taskbar as a separate entity
                int style = GetWindowLong(myHandle, GWL_STYLE);
                style = (style & ~WS_POPUP) | WS_CHILD;
                SetWindowLong(myHandle, GWL_STYLE, style);

                SetParent(myHandle, taskbarHandle);

                CalculateAndSetPosition(taskbarHandle, myHandle);
            }

            // for hover animation
            if (MainBorder.Background is not SolidColorBrush)
            {
                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during setup");
        }
    }

    private void UpdatePosition()
    {
        // Check premium status before allowing widget to be displayed
        if (!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked)
            return;

        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);

            if (taskbarHandle != IntPtr.Zero && interop.Handle != IntPtr.Zero)
            {
                CalculateAndSetPosition(taskbarHandle, interop.Handle);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during position update");
        }
    }

    private void CalculateAndSetPosition(IntPtr taskbarHandle, IntPtr myHandle)
    {
        // get DPI scaling
        if (_dpiScaleX == 0 && _dpiScaleY == 0)
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source == null || source.CompositionTarget == null)
            {
                // Window is not yet loaded or has been closed; cannot calculate DPI scaling
                return;
            }
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        // calculate widget width - use cached values if text hasn't changed
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

        double logicalWidth = Math.Max(_cachedTitleWidth, _cachedArtistWidth) + 40 * _scale; // add margin for cover image
        // maximum width limit, same as Windows native widget
        logicalWidth = Math.Min(logicalWidth, _nativeWidgetsPadding);

        SongTitle.Width = logicalWidth - 40 * _scale;
        SongArtist.Width = logicalWidth - 40 * _scale;

        int physicalWidth = (int)(logicalWidth * _dpiScaleX);
        int physicalHeight = (int)(this.Height * _dpiScaleY);

        // Get Taskbar dimensions
        RECT taskbarRect;
        GetWindowRect(taskbarHandle, out taskbarRect);
        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;

        // Centered vertically
        int physicalTop = (taskbarHeight - physicalHeight) / 2;

        int physicalLeft = 0;
        switch (SettingsManager.Current.TaskbarWidgetPosition) { 
            case 0: // left aligned with some padding (like native widgets)
                physicalLeft = 20; // maybe add automatic widget padding?
                if (SettingsManager.Current.TaskbarWidgetPadding)
                {
                    physicalLeft += _nativeWidgetsPadding;
                }
                break;
            case 1: // center of the taskbar
                physicalLeft = (taskbarRect.Right - taskbarRect.Left - physicalWidth) / 2;
                break;
            case 2: // right aligned next to system tray with tiny bit of padding
                IntPtr trayHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);
                if (trayHandle == IntPtr.Zero)
                {
                    // Fallback to left alignment or handle error appropriately
                    physicalLeft = 20;
                    break;
                }
                RECT trayRect;
                GetWindowRect(trayHandle, out trayRect);
                physicalLeft = trayRect.Left - physicalWidth - 1;
                break;
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
        if ((!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked))
        {
            Dispatcher.Invoke(() =>
            {
                Visibility = Visibility.Collapsed;
            });
            return;
        }

        if (title == "-" && artist == "-")
        {
            // no media playing, hide UI
            Dispatcher.Invoke(() =>
            {
                if (SettingsManager.Current.TaskbarWidgetHideCompletely)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                SongTitle.Text = "";
                SongArtist.Text = "";
                SongInfoStackPanel.Visibility = Visibility.Collapsed;
                SongImagePlaceholder.Symbol = Wpf.Ui.Controls.SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
                TopBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;

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
            SongTitle.Text = !String.IsNullOrEmpty(title) ? title : "-";
            SongArtist.Text = !String.IsNullOrEmpty(artist) ? artist : "-";

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
                BackgroundImage.Source = icon;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -2); // align image better when cover is present

                // start cross-fade if previous task is completed
                //if (_crossFadeTask.IsCompleted)
                //{
                //    _crossFadeTask = CrossFadeBackground(icon);
                //}
            }
            else
            {
                SongImagePlaceholder.Symbol = Wpf.Ui.Controls.SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
            }

            SongTitle.Visibility = Visibility.Visible;
            SongArtist.Visibility = Visibility.Visible;
            SongInfoStackPanel.Visibility = Visibility.Visible;
            BackgroundImage.Visibility = SettingsManager.Current.TaskbarWidgetBackgroundBlur ? Visibility.Visible : Visibility.Collapsed;
            Visibility = Visibility.Visible;

            UpdatePosition();
        });
    }

    //private Task CrossFadeBackground(BitmapImage newImage)
    //{
    //    try
    //    {
    //        BackgroundImageNext.Source = newImage;

    //        fadeIn.Completed += (s, e) =>
    //        {
    //            BackgroundImage.Source = newImage;
    //            BackgroundImageNext.Opacity = 0;
    //            BackgroundImageNext.Source = null;
    //            fadeIn.Completed -= (s, e2) => {  };
    //        };

    //        BackgroundImage.BeginAnimation(OpacityProperty, fadeOut);
    //        BackgroundImageNext.BeginAnimation(OpacityProperty, fadeIn);
    //        return Task.CompletedTask;
    //    }
    //    catch
    //    {
    //        // ignore errors
    //        return Task.CompletedTask;
    //    }
    //}
}