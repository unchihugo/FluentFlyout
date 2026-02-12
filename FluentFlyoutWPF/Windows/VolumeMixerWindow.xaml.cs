// Copyright © 2024-2026 The FluentFlyout Authors
//
// Portions of this code are derived from:
// - gpkgpk/HideVolumeOSD: https://github.com/gpkgpk/HideVolumeOSD
//
// Copyright © 2022 gpkgpk
// Modifications copyright © 2026 The FluentFlyout Authors
//
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.ViewModels;
using MicaWPF.Controls;
using NLog;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

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

    public VolumeMixerWindow()
    {
        DataContext = this;
        InitializeComponent();
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
                _mainWindow.OpenAnimation(this, true);

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
                ViewModel.SyncMasterFromDevice();
                if (!IsMouseOverWindow())
                {
                    await Task.Delay(SettingsManager.Current.VolumeControlDuration, token);
                    if (!IsMouseOverWindow())
                    {
                        _mainWindow.CloseAnimation(this);
                        _isHiding = true;
                        await Task.Delay(_mainWindow.getDuration());
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

    private void MicaWindow_Loaded(object sender, RoutedEventArgs e)
    {
        WindowHelper.SetTopmost(this);
        if (SettingsManager.Current.VolumeControlAboveMediaFlyout)
            _mainWindow.OpenAnimation(this, aboveReference: _mainWindow);
        else
            _mainWindow.OpenAnimation(this, true);
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
        while ((hwndXamlIsland = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "XamlExplorerHostIslandWindow", null)) != IntPtr.Zero)
        {
            if (hwndXamlIsland == IntPtr.Zero)
            {
                continue;
            }

            hwndOsd = NativeMethods.FindWindowEx(hwndXamlIsland, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", "DesktopWindowXamlSource");
            if (hwndOsd == IntPtr.Zero)
            {
                continue;
            }

            // check if the child window has the expected class name and title
            IntPtr hwndInputClass = NativeMethods.FindWindowEx(hwndOsd, IntPtr.Zero, "Windows.UI.Input.InputSite.WindowClass", null);
            if (hwndInputClass == IntPtr.Zero)
            {
                hwndOsd = IntPtr.Zero;
                continue;
            }

            NativeMethods.ShowWindow(hwndInputClass, 9); // SW_RESTORE
            if (NativeMethods.GetWindowRect(hwndInputClass, out NativeMethods.RECT rect))
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
        _nativeOsdOriginalExStyle = NativeMethods.GetWindowLong(_nativeOsdElement, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_nativeOsdElement, NativeMethods.GWL_EXSTYLE,
            _nativeOsdOriginalExStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT);
        NativeMethods.SetWindowPos(_nativeOsdElement, 0, -99999, -99999, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        NativeMethods.ShowWindow(_nativeOsdElement, 6); // SW_MINIMIZE
        Logger.Info("Successfully hid volume OSD.");
    }

    public static void ShowVolumeOsd()
    {
        if (_nativeOsdElement == IntPtr.Zero)
        {
            Logger.Warn("Cannot restore OSD because it was not found.");
            return;
        }

        NativeMethods.SetWindowLong(_nativeOsdElement, NativeMethods.GWL_EXSTYLE, _nativeOsdOriginalExStyle);
        NativeMethods.SetWindowPos(_nativeOsdElement, 0, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        NativeMethods.ShowWindow(_nativeOsdElement, 5); // SW_SHOW
        _nativeOsdElement = IntPtr.Zero;
        Logger.Info("Successfully restored volume OSD.");
    }

    private void AnimateExpandCollapse(bool expand)
    {
        int msDuration = _mainWindow.getDuration();
        var easing = msDuration > 0 ? _mainWindow.getEasingStyle(true) : null;
        var duration = new Duration(TimeSpan.FromMilliseconds(msDuration > 0 ? msDuration / 1.4 : 1));

        double expandedHeight;
        if (expand)
        {
            SessionsExpanded.Visibility = Visibility.Visible;
            SessionsPanel.UpdateLayout();
        }

        // measure desired size
        SessionsExpanded.Measure(new Size(ActualWidth, double.PositiveInfinity));
        expandedHeight = _collapsedHeight + SessionsExpanded.DesiredSize.Height;

        double targetHeight = expand ? expandedHeight : _collapsedHeight;
        double currentHeight = ActualHeight;
        double heightDelta = targetHeight - currentHeight;

        var chevronAnimation = new DoubleAnimation
        {
            To = expand ? 180 : 0,
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

        var topAnimation = new DoubleAnimation
        {
            From = Top,
            To = Top - heightDelta,
            Duration = duration,
            EasingFunction = easing
        };

        if (!expand)
        {
            heightAnimation.Completed += (s, e) =>
            {
                SessionsExpanded.Visibility = Visibility.Collapsed;
            };
        }

        Dispatcher.Invoke(() => {
            BeginAnimation(TopProperty, topAnimation);
            BeginAnimation(HeightProperty, heightAnimation);
        });
    }

    // TODO: other windows should use this too instead of IsMouseOver, consider moving to a helper class
    private bool IsMouseOverWindow()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
            return false;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
            return false;

        return cursor.X >= rect.Left && cursor.X <= rect.Right &&
               cursor.Y >= rect.Top && cursor.Y <= rect.Bottom;
    }
}