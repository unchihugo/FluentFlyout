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
using System.Windows;
using System.Windows.Media.Animation;
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

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    // one day we might want to convert these to an interface
    public async void ShowFlyout()
    {
        if (FullscreenDetector.IsFullscreenApplicationRunning())
            return;

        long currentTime = Environment.TickCount64;

        if (currentTime - _lastFlyoutTime < _flyoutCooldown.TotalMilliseconds)
        {
            return;
        }

        _lastFlyoutTime = currentTime;

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
                Width = _mainWindow.Width;
            else
                Width = _normalWidth;

            if (aboveMedia)
                _mainWindow.OpenAnimation(this, aboveReference: _mainWindow);
            else
                _mainWindow.OpenAnimation(this, alwaysBottom: true);

            Show();
            //WindowHelper.SetNoActivate(this);
            WindowHelper.SetTopmost(this);
        }

        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token); // check if mouse is over every 100ms
                // update master volume again because it can be slow to update when coming from a hardware key press
                ViewModel.SyncMasterVolume();

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
                        _mainWindow.CloseAnimation(this);
                        _isHiding = true;
                        await Task.Delay(MainWindow.getDuration());
                        if (_isHiding == false) return;

                        WindowHelper.SetVisibility(this, false);
                        ViewModel.IsExpanded = false;
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

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VolumeMixerViewModel.IsExpanded))
        {
            AnimateExpandCollapse(ViewModel.IsExpanded);
        }
    }

    // derived from gpkgpk/HideVolumeOSD: https://github.com/gpkgpk/HideVolumeOSD
    private static void HideVolumeOsd()
    {
        // find widget in XAML
        IntPtr hwndXamlIsland, hwndOsd = IntPtr.Zero;
        while ((hwndXamlIsland = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "XamlExplorerHostIslandWindow", null)) != IntPtr.Zero)
        {
            if (hwndXamlIsland == IntPtr.Zero)
            {
                continue;
            }

            hwndOsd = FindWindowEx(hwndXamlIsland, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", "DesktopWindowXamlSource");
            if (hwndOsd == IntPtr.Zero)
            {
                continue;
            }

            // check if the child window has the expected class name and title
            IntPtr hwndInputClass = FindWindowEx(hwndOsd, IntPtr.Zero, "Windows.UI.Input.InputSite.WindowClass", null);
            if (hwndInputClass == IntPtr.Zero)
            {
                hwndOsd = IntPtr.Zero;
                continue;
            }

            ShowWindow(hwndInputClass, 9); // SW_RESTORE
            if (GetWindowRect(hwndInputClass, out RECT rect))
            {
                if (rect.Top == 0 && rect.Left == 0 && rect.Bottom == 0 && rect.Right == 0)
                {
                    hwndOsd = IntPtr.Zero;
                }
                else break;
            }
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

        bool isTop = SettingsManager.Current.Position switch
        {
            3 or 4 or 5 => true,
            _ => false
        };

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