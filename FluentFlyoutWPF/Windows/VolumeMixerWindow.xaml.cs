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
using System.Windows.Media.Animation;

namespace FluentFlyoutWPF.Windows;

// TODO: the native volume OSD is hit testable even when hidden
// TODO: make whole window react to IsMouseOver (margins are ignored)

/// <summary>
/// Interaction logic for VolumeMixerWindow.xaml
/// </summary>
public partial class VolumeMixerWindow : MicaWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    public VolumeMixerViewModel ViewModel { get; } = new();
    public UserSettings UserSettings => SettingsManager.Current;

    private IntPtr _nativeOsdElement = IntPtr.Zero;
    private CancellationTokenSource _cts; 
    private MainWindow _mainWindow;
    private readonly double _collapsedHeight = 50;
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
            else
            {
                NativeMethods.SetWindowPos(_nativeOsdElement, 0, -99999, -99999, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
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

            Show();
            //WindowHelper.SetNoActivate(this);
            WindowHelper.SetTopmost(this);
            _mainWindow.OpenAnimation(this, true);
        }

        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token); // check if mouse is over every 100ms
                if (!IsMouseOver)
                {
                    await Task.Delay(3000, token);
                    if (!IsMouseOver)
                    {
                        _mainWindow.CloseAnimation(this, true);
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
    private void HideVolumeOsd()
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
        NativeMethods.SetWindowPos(_nativeOsdElement, 0, -99999, -99999, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        Logger.Info("Successfully hid volume OSD.");
    }

    private void ShowVolumeOsd()
    {
        if (_nativeOsdElement == IntPtr.Zero)
        {
            Logger.Warn("Cannot restore OSD because it was not found.");
            return;
        }

        NativeMethods.SetWindowPos(_nativeOsdElement, 0, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
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
            SessionsPanel.Visibility = Visibility.Visible;
            SessionsPanel.UpdateLayout();
        }

        // measure desired size
        SessionsPanel.Measure(new Size(ActualWidth, double.PositiveInfinity));
        expandedHeight = _collapsedHeight + SessionsPanel.DesiredSize.Height;

        double targetHeight = expand ? expandedHeight : _collapsedHeight;
        double currentHeight = ActualHeight;
        double heightDelta = targetHeight - currentHeight;

        var chevronAnimation = new DoubleAnimation
        {
            To = expand ? 180 : 0,
            Duration = duration,
            EasingFunction = easing
        };
        ChevronRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, chevronAnimation);

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
                SessionsPanel.Visibility = Visibility.Collapsed;
            };
        }

        BeginAnimation(TopProperty, topAnimation);
        BeginAnimation(HeightProperty, heightAnimation);
    }
}