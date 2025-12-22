using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using WindowsMediaController;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

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
    private IntPtr _trayHandle;
    private AutomationElement? _widgetElement;
    // reference to main window for flyout functions
    private MainWindow? _mainWindow;
    private bool _isPaused;
    private int _recoveryAttempts = 0;
    private int _maxRecoveryAttempts = 5;
    //private Task _crossFadeTask = Task.CompletedTask;

    public TaskbarWindow()
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

        _hitTestTransparent = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));

        // initialize here in case we want to restart the window
        _dpiScaleX = 0;
        _dpiScaleY = 0;

        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1500); // slow auto-update for display changes
        _timer.Tick += (s, e) => UpdatePosition();
        _timer.Start();

        Show();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
        _mainWindow = (MainWindow)Application.Current.MainWindow;
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
        if (SettingsManager.Current.TaskbarWidgetTriggerType == 0 || String.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

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

        if (SettingsManager.Current.TaskbarWidgetTriggerType == 1)
        {
            var mainWindow = (MainWindow)Application.Current.MainWindow;
            mainWindow.ShowMediaFlyout();
        }
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (SettingsManager.Current.TaskbarWidgetTriggerType == 0 || String.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

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
        if (SettingsManager.Current.TaskbarWidgetTriggerType == 2)
        {
            // flyout main flyout when clicked
            var mainWindow = (MainWindow)Application.Current.MainWindow;
            mainWindow.ShowMediaFlyout();
        }
    }

    private void SetupWindow()
    {
        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr myHandle = interop.Handle;

            Background = _hitTestTransparent; // ensures that non-content areas also trigger MouseEnter event

            IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);

            // This prevents the window from trying to float above the taskbar as a separate entity
            int style = GetWindowLong(myHandle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD;
            SetWindowLong(myHandle, GWL_STYLE, style);

            SetParent(myHandle, taskbarHandle);

            CalculateAndSetPosition(taskbarHandle, myHandle);

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

            if (interop.Handle == IntPtr.Zero) // window handle lost, try to reset
            {
                _timer.Stop();

                if (_recoveryAttempts >= _maxRecoveryAttempts)
                {
                    Logger.Warn("Taskbar Widget window handle is zero and recovery already attempted, stopping updates.");
                    return; // already tried recovery, don't loop
                }

                Logger.Warn("Taskbar Widget window handle is zero, attempting recovery...");

                Dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(1000); // delay before recovery to let taskbar stabilize
                    try
                    {
                        _mainWindow?.RecreateTaskbarWindow();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to signal MainWindow to recover Taskbar Widget window");
                        _recoveryAttempts++;
                    }
                }, DispatcherPriority.Background);

                return;
            }

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
            var dpiScale = VisualTreeHelper.GetDpi(this);

            _dpiScaleX = dpiScale.DpiScaleX;
            _dpiScaleY = dpiScale.DpiScaleY;
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

        // add space for playback controls if enabled
        if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
        {
            logicalWidth += (int)(110 * _scale);
        }

        int physicalWidth = (int)(logicalWidth * _dpiScaleX);
        int physicalHeight = (int)(40 * _dpiScaleY); // default height

        // Get Taskbar dimensions
        RECT taskbarRect;
        GetWindowRect(taskbarHandle, out taskbarRect);
        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;

        // Centered vertically
        int physicalTop = (taskbarHeight - physicalHeight) / 2;

        int physicalLeft = 0;
        switch (SettingsManager.Current.TaskbarWidgetPosition)
        {
            case 0: // left aligned with some padding (like native widgets)
                physicalLeft = 20;
                if (SettingsManager.Current.TaskbarWidgetPadding) // automatic widget padding to the left
                {
                    try
                    {
                        // find widget button in XAML
                        (bool found, Rect widgetRect) = GetTaskbarWidgetRect(taskbarHandle);

                        // make sure it's on the left side, otherwise ignore (widget might be to the right)
                        if (found && widgetRect.Right < taskbarRect.Right / 2)
                            physicalLeft = (int)(widgetRect.Right) + 2; // add small padding
                    }
                    catch (Exception ex)
                    {
                        // fallback to default padding
                        Logger.Warn(ex, "Failed to get Widgets button position.");
                        physicalLeft += _nativeWidgetsPadding + 2;
                    }
                }
                break;

            case 1: // center of the taskbar
                physicalLeft = (taskbarRect.Right - taskbarRect.Left - physicalWidth) / 2;
                break;

            case 2: // right aligned next to system tray with tiny bit of padding
                try
                {
                    if (SettingsManager.Current.TaskbarWidgetPadding) // automatic widget padding to the right
                    {
                        try
                        {
                            // find widget button in XAML
                            (bool found, Rect widgetRect) = GetTaskbarWidgetRect(taskbarHandle);

                            // make sure it's on the right side, otherwise ignore (widget might be to the left)
                            if (found && widgetRect.Left > taskbarRect.Right / 2)
                            {
                                physicalLeft = (int)(widgetRect.Left) - 2 - physicalWidth; // left of widget
                                break; // early exit so we don't move it back next to tray below
                            }
                        }
                        catch (Exception ex) // catch exception when getting widget position
                        {
                            Logger.Warn(ex, "Failed to get Widgets button position.");
                        }
                    }

                    if (_trayHandle == IntPtr.Zero)
                    {
                        _trayHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);
                    }

                    if (_trayHandle == IntPtr.Zero)
                    {
                        // Fallback to left alignment
                        physicalLeft = 20;
                        break;
                    }
                    RECT trayRect;
                    GetWindowRect(_trayHandle, out trayRect);
                    physicalLeft = trayRect.Left - physicalWidth - 1;
                }
                catch (Exception ex)
                {
                    // Fallback to left alignment
                    Logger.Warn(ex, "Failed to get System Tray position.");
                    physicalLeft = 20;
                }
                break;
        }

        // TODO: Finish: Update visibility to force layout update after DPI/monitor change
        //if (SongInfoStackPanel.Visibility == Visibility.Visible)
        //{
        //    SongInfoStackPanel.Visibility = Visibility.Collapsed;
        //    SongInfoStackPanel.Visibility = Visibility.Visible;
        //}

        // Apply using SetWindowPos (Bypassing WPF layout engine)
        SetWindowPos(myHandle, IntPtr.Zero,
                 physicalLeft, physicalTop,
                 physicalWidth, physicalHeight,
                 SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_SHOWWINDOW);
    }

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
    {
        // Check premium status - hide widget if not unlocked
        if ((!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked))
        {
            if (_timer.IsEnabled) // pause timer to save resources
                _timer.Stop();

            Dispatcher.Invoke(() =>
            {
                Visibility = Visibility.Collapsed;
            });
            return;
        }

        if (!_timer.IsEnabled)
            _timer.Start();

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

                ControlsStackPanel.Visibility = Visibility.Collapsed;
                SongTitle.Text = "";
                SongArtist.Text = "";
                SongInfoStackPanel.Visibility = Visibility.Collapsed;
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
                TopBorder.BorderBrush = Brushes.Transparent;

                UpdatePosition();
                Visibility = Visibility.Visible;
            });
            return;
        }

        _isPaused = false;
        if (playbackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            _isPaused = true;
        }

        // adjust UI based on available controls
        Dispatcher.Invoke(() =>
        {
            if (SettingsManager.Current.TaskbarWidgetControlsEnabled && playbackControls != null)
            {
                PreviousButton.IsHitTestVisible = playbackControls.IsPreviousEnabled;
                PlayPauseButton.IsHitTestVisible = playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled;
                NextButton.IsHitTestVisible = playbackControls.IsNextEnabled;

                PreviousButton.Opacity = playbackControls.IsPreviousEnabled ? 1 : 0.5;
                PlayPauseButton.Opacity = (playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled) ? 1 : 0.5;
                NextButton.Opacity = playbackControls.IsNextEnabled ? 1 : 0.5;
            }
            else
            {
                PreviousButton.IsHitTestVisible = false;
                PlayPauseButton.IsHitTestVisible = false;
                NextButton.IsHitTestVisible = false;

                PreviousButton.Opacity = 0.5;
                NextButton.Opacity = 0.5;
                PlayPauseButton.Opacity = 0.5;
            }
        });

        Dispatcher.Invoke(() =>
        {
            if (SongTitle.Text != title && SongArtist.Text != artist)
            {
                // changed info
                if (SettingsManager.Current.TaskbarWidgetAnimated)
                {
                    AnimateEntrance();
                }
            }

            SongTitle.Text = !String.IsNullOrEmpty(title) ? title : "-";
            SongArtist.Text = !String.IsNullOrEmpty(artist) ? artist : "-";

            if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
            {
                PlayPauseButton.Icon = _isPaused ? new SymbolIcon(SymbolRegular.Play24, filled: true) : new SymbolIcon(SymbolRegular.Pause24, filled: true);
            }

            if (icon != null)
            {
                if (_isPaused)
                { // show pause icon overlay
                    SongImagePlaceholder.Symbol = SymbolRegular.Pause24;
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
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
            }

            SongTitle.Visibility = Visibility.Visible;
            SongArtist.Visibility = !String.IsNullOrEmpty(artist) ? Visibility.Visible : Visibility.Collapsed; // hide artist if it's not available
            SongInfoStackPanel.Visibility = Visibility.Visible;
            BackgroundImage.Visibility = SettingsManager.Current.TaskbarWidgetBackgroundBlur ? Visibility.Visible : Visibility.Collapsed;

            // on top of XAML visibility binding (XAML binding only hides when disabled in settings)
            if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
            {
                ControlsStackPanel.Visibility = Visibility.Visible;
            }
            
            Visibility = Visibility.Visible;

            // defer UpdatePosition to allow WPF layout to complete first
            Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);
        });
    }

    private async void AnimateEntrance()
    {
        try
        {
            int msDuration = _mainWindow != null ? _mainWindow.getDuration() : 300;

            // opacity and left to right animation for SongInfoStackPanel
            DoubleAnimation opacityAnimation = new()
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation translateAnimation = new()
            {
                From = -10,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Apply animations
            SongInfoStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            TranslateTransform translateTransform = new();
            SongInfoStackPanel.RenderTransform = translateTransform;
            translateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);

            // don't play ControlsStackPanel animation if it's not enabled
            if (!SettingsManager.Current.TaskbarWidgetControlsEnabled)
                return;

            ControlsStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            TranslateTransform translateTransform2 = new();
            ControlsStackPanel.RenderTransform = translateTransform2;
            translateTransform2.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during entrance animation");
        }
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

    /// <summary>
    /// Attempts to locate the Windows taskbar widgets button and retrieves its bounding rectangle.
    /// </summary>
    /// <returns>A tuple where the first value indicates whether the widgets button was found (<see langword="true"/> if found;
    /// otherwise, <see langword="false"/>), and the second value is the bounding rectangle of the button if found, or
    /// <see cref="Rect.Empty"/> if not found.</returns>
    private (bool, Rect) GetTaskbarWidgetRect(IntPtr taskbarHandle)
    {
        try
        {
            // find widget button in XAML
            if (_widgetElement == null)
            {
                AutomationElement root = AutomationElement.FromHandle(taskbarHandle);

                _widgetElement = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "WidgetsButton"));
            }

            if (_widgetElement == null) // widget most likely disabled
                return (false, Rect.Empty);

            try
            {
                Rect widgetRect = _widgetElement.Current.BoundingRectangle;

                if (widgetRect == Rect.Empty) // widget shown before but most likely disabled now
                    return (false, Rect.Empty);

                return (true, widgetRect);
            }
            catch (ElementNotAvailableException)
            {
                // element became stale, reset cache
                Logger.Warn("Taskbar Widgets button element became stale, resetting cache.");
                _widgetElement = null;
                return (false, Rect.Empty);
            }
        }
        catch (COMException ex)
        {
            Logger.Error(ex, "COM error retrieving taskbar widgets button Rect.");
            return (false, Rect.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error retrieving taskbar widgets button Rect.");
            return (false, Rect.Empty);
        }
    }

    // event handlers for media control buttons
    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var mediaManager = _mainWindow.mediaManager;
        if (mediaManager == null) return;

        var focusedSession = mediaManager.GetFocusedSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var mediaManager = _mainWindow.mediaManager;
        if (mediaManager == null) return;

        var focusedSession = mediaManager.GetFocusedSession();
        if (focusedSession == null) return;

        if (_isPaused) // paused
        {
            await focusedSession.ControlSession.TryPlayAsync();
        }
        else // playing
        {
            await focusedSession.ControlSession.TryPauseAsync();
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var mediaManager = _mainWindow.mediaManager;
        if (mediaManager == null) return;

        var focusedSession = mediaManager.GetFocusedSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TrySkipNextAsync();
    }
}