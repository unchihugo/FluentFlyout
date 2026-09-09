// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

// Portions of this code are derived from:
// - gpkgpk/HideVolumeOSD: https://github.com/gpkgpk/HideVolumeOSD
//
// Copyright (c) 2022 gpkgpk
// Modifications copyright (c) 2026 The FluentFlyout Authors

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.ViewModels;
using MicaWPF.Controls;
using NLog;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF.Windows;

/// <summary>
/// Interaction logic for VolumeMixerWindow.xaml
/// </summary>
public partial class VolumeMixerWindow : MicaWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    public VolumeMixerViewModel ViewModel { get; } = new();
    public UserSettings UserSettings => SettingsManager.Current;

    private static IntPtr _nativeOsdElement = IntPtr.Zero;
    private static int _nativeOsdOriginalExStyle;
    private CancellationTokenSource _cts;
    private MainWindow _mainWindow;
    private readonly double _collapsedHeight = 50;
    private readonly double _normalWidth;
    private bool _isHiding = true;

    private long _lastFlyoutTime = 0;
    private readonly TimeSpan _flyoutCooldown = TimeSpan.FromMilliseconds(500);
    private readonly DispatcherTimer _autoHideExtendTimer;

    public VolumeMixerWindow()
    {
        DataContext = this;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);
        CustomWindowChrome.CaptionHeight = 0;
        CustomWindowChrome.UseAeroCaptionButtons = false;
        CustomWindowChrome.GlassFrameThickness = new Thickness(0);

        _mainWindow = (MainWindow)Application.Current.MainWindow;
        _cts = new CancellationTokenSource();
        _normalWidth = Width;

        _autoHideExtendTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, SettingsManager.Current.VolumeControlDuration)) };
        _autoHideExtendTimer.Tick += OnAutoHideExtendTimerTick;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.SessionVolumeChanged += OnSessionVolumeChanged;
        ViewModel.EndpointVolumeChanged += OnEndpointVolumeChanged;
    }

    /// <summary>
    /// Bluetooth / AVRCP volume changes never produce VK_VOLUME. When the flyout
    /// is already visible, reset the auto-hide timer in place (cooldown still
    /// blocks a reopen while hidden). Otherwise present the flyout (#1119).
    /// </summary>
    private void OnEndpointVolumeChanged(object? sender, EventArgs e)
    {
        if (!SettingsManager.Current.VolumeControlEnabled)
            return;

        if (!_isHiding)
        {
            RestartAutoHideTimer();
            return;
        }

        ShowFlyout();
    }

    /// <summary>
    /// Extends the visible flyout's auto-hide without reopening it. Restarts a
    /// DispatcherTimer rather than bypassing the 500 ms show-cooldown, so
    /// hidden-state floods still cannot reopen the window.
    /// </summary>
    private void RestartAutoHideTimer()
    {
        int durationMs = Math.Max(1, SettingsManager.Current.VolumeControlDuration);
        _autoHideExtendTimer.Stop();
        _autoHideExtendTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
        _autoHideExtendTimer.Start();

        try
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        _ = RunAutoHideLoopAsync(_cts.Token);
    }

    private void OnAutoHideExtendTimerTick(object? sender, EventArgs e)
    {
        _autoHideExtendTimer.Stop();
    }

    // one day we might want to convert these to an interface
    public void ShowFlyout(bool startExpanded = false)
    {
        if (FullscreenDetector.IsFullscreenApplicationRunning())
            return;

        long currentTime = Environment.TickCount64;

        if (currentTime - _lastFlyoutTime < _flyoutCooldown.TotalMilliseconds)
        {
            return;
        }

        _lastFlyoutTime = currentTime;

        // Everything up to the auto-hide loop below used to run outside any
        // try/catch. ShowFlyout is `async void`, so a single throw here (a
        // blur/DWM call failing, placement running while the window is being
        // torn down, a disposed CTS) went straight to the runtime and killed the
        // process with no log entry - the abnormal exit on volume key press
        // reported in #1075.
        try
        {
            if (_isHiding)
            {
                if (_nativeOsdElement == IntPtr.Zero)
                {
                    _ = Task.Run(() =>
                    {
                        HideVolumeOsd();
                    });
                }

                _isHiding = false;
                if (SettingsManager.Current.VolumeMixerAcrylicWindowEnabled)
                {
                    WindowBlurHelper.EnableBlur(this);
                }
                else
                {
                    WindowBlurHelper.DisableBlur(this);
                }

                // refresh all data
                ViewModel.OnPollTick(null, EventArgs.Empty);

                bool aboveMedia = SettingsManager.Current.VolumeControlAboveMediaFlyout;
                if (aboveMedia)
                {
                    Width = _mainWindow.Width;
                    _mainWindow.OpenAnimation(this, aboveReference: _mainWindow, reserveNativeVolumeOsdSpace: true);
                }
                else
                {
                    Width = _normalWidth;
                    _mainWindow.OpenAnimation(this, alwaysBottom: true);
                }

                Show();
                WindowHelper.SetTopmost(this);

                _ = Task.Run(() =>
                {
                    Thread.Sleep(MainWindow.getDuration());
                    try
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (startExpanded) ViewModel.IsExpanded = true;
                        });
                    }
                    catch (Exception ex)
                    {
                        // dispatcher shut down while we waited out the animation
                        Logger.Debug(ex, "Deferred volume flyout expand failed");
                    }
                });
            }
            else
            {
                // only expand if the flyout isn't expanded already
                if (startExpanded) ViewModel.IsExpanded = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to show volume flyout");
            return;
        }

        Logger.Info("Volume flyout shown");
        RestartAutoHideTimer();
    }

    private async Task RunAutoHideLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token); // check if mouse is over every 100ms
                // update master volume again because it can be slow to update when coming from a hardware key press
                ViewModel.SyncMasterFromDevice();

                bool mouseOverThis = WindowHelper.IsMouseOverWindow(this);
                bool mouseOverMedia = SettingsManager.Current.VolumeControlAboveMediaFlyout
                    && _mainWindow.Visibility == Visibility.Visible
                    && WindowHelper.IsMouseOverWindow(_mainWindow); // sync with media flyout

                if (!mouseOverThis && !mouseOverMedia)
                {
                    await Task.Delay(SettingsManager.Current.VolumeControlDuration, token);

                    mouseOverThis = WindowHelper.IsMouseOverWindow(this);
                    mouseOverMedia = SettingsManager.Current.VolumeControlAboveMediaFlyout
                        && _mainWindow.Visibility == Visibility.Visible
                        && WindowHelper.IsMouseOverWindow(_mainWindow);

                    if (!mouseOverThis && !mouseOverMedia)
                    {
                        _autoHideExtendTimer.Stop();
                        _mainWindow.CloseAnimation(this);
                        _isHiding = true;
                        await Task.Delay(MainWindow.getDuration());
                        if (_isHiding == false) return;

                        WindowHelper.SetVisibility(this, false);
                        ViewModel.IsExpanded = false;
                        Logger.Info("Volume flyout hidden");
                        break;
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            // do nothing
        }
        catch (Exception ex)
        {
            // Never let the auto-hide loop take the process down: an async void
            // throw here is an abnormal exit with no further logging.
            Logger.Error(ex, "Volume flyout loop failed, hiding flyout");
            try
            {
                _autoHideExtendTimer.Stop();
                _isHiding = true;
                WindowHelper.SetVisibility(this, false);
                ViewModel.IsExpanded = false;
            }
            catch (Exception hideEx)
            {
                Logger.Debug(hideEx, "Volume flyout emergency hide failed");
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VolumeMixerViewModel.IsExpanded))
        {
            AnimateExpandCollapse(ViewModel.IsExpanded);
        }
    }

    private void OnSessionVolumeChanged(object? sender, EventArgs e)
    {
        _mainWindow.taskbarWindow?.RefreshAppVolumeTooltip();
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _autoHideExtendTimer.Stop();
            _autoHideExtendTimer.Tick -= OnAutoHideExtendTimerTick;
            _cts.Cancel();
            _cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.SessionVolumeChanged -= OnSessionVolumeChanged;
        ViewModel.EndpointVolumeChanged -= OnEndpointVolumeChanged;
        ViewModel.Dispose();
        base.OnClosed(e);
    }

    // derived from gpkgpk/HideVolumeOSD: https://github.com/gpkgpk/HideVolumeOSD
    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    private const uint SPI_GETWORKAREA = 0x0048;

    private static void HideVolumeOsd()
    {
        // Already hiding an island: re-enumerating would either re-hide the
        // same one or, once our island was moved off-screen, run headlong
        // into the next candidate.
        if (_nativeOsdElement != IntPtr.Zero)
            return;

        // Enumerate top-level XAML islands properly: FindWindowEx with a NULL
        // child-after handle always returns the FIRST match, so the previous
        // code re-examined the same window forever (100% CPU spin) whenever it
        // didn't meet the criteria, and could hide the wrong island's window.
        IntPtr hwndOsd = IntPtr.Zero;
        IntPtr hwndXamlIsland = IntPtr.Zero;

        RECT workArea = default;
        bool workAreaValid = SystemParametersInfo(SPI_GETWORKAREA, 0, ref workArea, 0);

        while ((hwndXamlIsland = FindWindowEx(IntPtr.Zero, hwndXamlIsland, "XamlExplorerHostIslandWindow", null)) != IntPtr.Zero)
        {
            IntPtr hwndBridge = IntPtr.Zero;
            while ((hwndBridge = FindWindowEx(hwndXamlIsland, hwndBridge, "Windows.UI.Composition.DesktopWindowContentBridge", "DesktopWindowXamlSource")) != IntPtr.Zero)
            {
                // check if the child window has the expected class name and title
                IntPtr hwndInputClass = FindWindowEx(hwndBridge, IntPtr.Zero, "Windows.UI.Input.InputSite.WindowClass", null);
                if (hwndInputClass == IntPtr.Zero)
                {
                    continue;
                }

                ShowWindow(hwndInputClass, 9); // SW_RESTORE
                if (GetWindowRect(hwndInputClass, out RECT rect))
                {
                    if (rect.Top == 0 && rect.Left == 0 && rect.Bottom == 0 && rect.Right == 0)
                    {
                        continue;
                    }

                    // IME flyouts and other shell surfaces share these class
                    // names; only the actual volume OSD (bottom-center of the
                    // primary work area) may be hidden.
                    if (!IsVolumeOsdRect(rect, workArea, workAreaValid))
                    {
                        Logger.Debug("Skipping island window that is not at the volume OSD position.");
                        continue;
                    }

                    hwndOsd = hwndBridge;
                    break;
                }
            }

            if (hwndOsd != IntPtr.Zero)
                break;
        }

        if (hwndOsd == IntPtr.Zero)
        {
            Logger.Warn("OSD window not found.");
            return;
        }

        // the parent owns the hit-test region on the desktop
        _nativeOsdElement = hwndXamlIsland;
        _nativeOsdOriginalExStyle = GetWindowLong(_nativeOsdElement, GWL_EXSTYLE);
        SetWindowLong(_nativeOsdElement, GWL_EXSTYLE,
            _nativeOsdOriginalExStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);
        SetWindowPos(_nativeOsdElement, 0, -99999, -99999, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        ShowWindow(_nativeOsdElement, SW_MINIMIZE);
        Logger.Info("Successfully hid volume OSD.");
    }

    /// <summary>
    /// The native volume OSD is a XamlExplorerHostIslandWindow docked to the
    /// bottom-center of the primary work area. IME flyouts and other shell
    /// surfaces reuse the same class names, so a candidate anywhere else must
    /// not be hidden.
    /// </summary>
    private static bool IsVolumeOsdRect(RECT rect, RECT workArea, bool workAreaValid)
    {
        if (!workAreaValid)
            return true; // fail open: keep the previous behavior when the work area can't be queried

        double workWidth = workArea.Right - workArea.Left;
        double workHeight = workArea.Bottom - workArea.Top;
        if (workWidth <= 0 || workHeight <= 0)
            return true;

        double centerX = (rect.Left + rect.Right) / 2.0;
        double workCenterX = (workArea.Left + workArea.Right) / 2.0;

        // horizontally centered on the primary work area
        if (Math.Abs(centerX - workCenterX) > workWidth * 0.2)
            return false;

        // and in its bottom band (the OSD pops up just above the taskbar)
        return rect.Bottom > workArea.Top + workHeight * 0.5 && rect.Top >= workArea.Top;
    }

    /// <summary>
    /// Re-hides the native volume OSD after an Explorer restart. Explorer
    /// recreates the OSD window (new HWND), so the previously hidden handle is
    /// dead and the native flyout pops back until something re-hides it.
    /// Retries briefly since the island can lag behind the taskbar.
    /// </summary>
    public static void RehideVolumeOsdAfterExplorerRestart()
    {
        _nativeOsdElement = IntPtr.Zero;
        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    if (attempt > 0)
                        await Task.Delay(2000);
                    HideVolumeOsd();
                    if (_nativeOsdElement != IntPtr.Zero)
                        return;
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Native OSD re-hide attempt failed");
                }
            }
            Logger.Warn("Could not re-hide native volume OSD after Explorer restart; will retry on next volume key press");
        });
    }

    public static void ShowVolumeOsd()
    {
        if (_nativeOsdElement == IntPtr.Zero)
        {
            Logger.Warn("Did not try to restore OSD because it was either not found or was not hidden.");
            return;
        }

        SetWindowLong(_nativeOsdElement, GWL_EXSTYLE, _nativeOsdOriginalExStyle);
        SetWindowPos(_nativeOsdElement, 0, 0, 0, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        ShowWindow(_nativeOsdElement, SW_RESTORE);
        _nativeOsdElement = IntPtr.Zero;
        Logger.Info("Successfully restored volume OSD.");
    }

    private void AnimateExpandCollapse(bool expand)
    {
        int msDuration = MainWindow.getDuration();
        var easing = msDuration > 0 ? _mainWindow.getEasingStyle(true) : null;
        var duration = new Duration(TimeSpan.FromMilliseconds(msDuration > 0 ? msDuration / 1.4 : 1));

        bool isTop = false;

        // check if the media flyout is at the top or bottom of the screen if applicable
        if (SettingsManager.Current.VolumeControlAboveMediaFlyout)
        {
            isTop = SettingsManager.Current.Position switch
            {
                3 or 4 or 5 => true,
                _ => false
            };
        }

        double expandedHeight;
        if (expand)
        {
            SessionsExpanded.Visibility = Visibility.Visible;
            SessionsSeparator.Visibility = Visibility.Visible;
            SessionsPanel.UpdateLayout();
        }

        // measure desired size
        SessionsExpanded.Measure(new Size(ActualWidth, double.PositiveInfinity));
        expandedHeight = _collapsedHeight + Math.Min(SessionsExpanded.DesiredSize.Height, 220);

        double targetHeight = expand ? expandedHeight : _collapsedHeight;
        double currentHeight = ActualHeight;
        double heightDelta = targetHeight - currentHeight;

        // When at the top, chevron points down (0°) when collapsed and up (180°) when expanded.
        // When at the bottom, chevron points up (180°) when expanded and down (0°) when collapsed.
        var chevronAnimation = new DoubleAnimation
        {
            To = isTop ? (expand ? 0 : 180) : (expand ? 180 : 0),
            Duration = duration,
            EasingFunction = easing
        };
        Dispatcher.Invoke(() =>
        {
            ChevronRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, chevronAnimation);
        });

        var heightAnimation = new DoubleAnimation
        {
            From = currentHeight,
            To = targetHeight,
            Duration = duration,
            EasingFunction = easing
        };

        // When at the top, the window grows downward so Top stays fixed.
        // When at the bottom, the window grows upward so Top shifts up by heightDelta.
        var topAnimation = new DoubleAnimation
        {
            From = Top,
            To = isTop ? Top : Top - heightDelta,
            Duration = duration,
            EasingFunction = easing
        };

        if (!expand)
        {
            heightAnimation.Completed += (s, e) =>
            {
                SessionsExpanded.Visibility = Visibility.Collapsed;
                SessionsSeparator.Visibility = Visibility.Collapsed;
            };
        }

        Dispatcher.Invoke(() =>
        {
            BeginAnimation(TopProperty, topAnimation);
            BeginAnimation(HeightProperty, heightAnimation);
        });
    }
}