// Copyright � 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Helpers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluentFlyout.Controls;

/// <summary>
/// Interaction logic for TaskbarVisualizerControl.xaml
/// </summary>
public partial class TaskbarVisualizerControl : UserControl
{
    // reference to main window for flyout functions
    private static readonly Visualizer visualizer = new();

    // scroll-to-volume state
    private float _pendingVolumeChange = 0f;
    private const float VolumeChangeThreshold = 0.005f; // minimum batched change before writing to Core Audio
    private const float MouseVolumeStep = 0.02f;        // flat 2% per mouse notch
    private const float OsVolumeStep = 0.02f;           // Windows multimedia key step (used for OSD pre-offset)
    private const float TouchpadVolumeScale = 0.10f;    // quadratic velocity curve scale for touchpad

    public TaskbarVisualizerControl()
    {
        InitializeComponent();

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

        if (SettingsManager.Current.TaskbarVisualizerEnabled)
        {
            visualizer.Start();
        }

        VisualizerContainer.Source = visualizer.Bitmap;

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
    }

    public static void OnTaskbarVisualizerEnabledChanged(bool value)
    {
        if (visualizer == null)
            return;

        if (value)
        {
            visualizer.Start();
        }
        else
        {
            visualizer.Stop();
        }
    }

    public static void DisposeVisualizer()
    {
        if (visualizer == null)
            return;

        visualizer.Dispose();
    }

    // TODO: The following mouse events are almost the same as the ones in TaskbarWidgetControl.xaml.cs.
    // We should find a way to unify these methods instead of duplicating them.

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

        SolidColorBrush targetBackgroundBrush;
        // hover effects with animations, hard-coded colors because I can't find the resource brushes
        WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
        bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;
        if (isDark)
        { // dark mode
            targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
            TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };
        }
        else
        { // light mode
            targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) { Opacity = 0.6 };
            TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 1 };
        }

        // Animate background
        var backgroundAnimation = new ColorAnimation
        {
            To = targetBackgroundBrush.Color,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = targetBackgroundBrush.Opacity,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // rare case where background is not a SolidColorBrush after SetupWindow
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

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
        // only continue when the visualizer is clickable and actually has content
        // otherwise it would show an empty container to click on which is weird
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

        // open settings when clicked
        SettingsWindow.ShowInstance("TaskbarVisualizerPage");
    }

    private void Grid_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarVisualizerEnabled || !SettingsManager.Current.TaskbarVisualizerHasContent || !SettingsManager.Current.TaskbarVisualizerScrollVolume)
            return;

        if (e.Delta == 0) return;

        // Touchpad sends fractional deltas (not multiples of 120); mouse always sends exact multiples.
        bool isTouchpad = e.Delta % 120 != 0;
        float increment;

        if (isTouchpad)
        {
            // Velocity curve: slow scroll = fine control, fast swipe = larger step.
            // Uses a signed quadratic on the delta normalised to one mouse notch (120 units).
            // At delta=30 this gives ~0.006 per event; at delta=80 it gives ~0.044 per event.
            float normalized = e.Delta / 120f;
            increment = -(normalized * Math.Abs(normalized) * TouchpadVolumeScale);
        }
        else
        {
            // Mouse: flat 2% per notch regardless of how fast the wheel spins.
            increment = (e.Delta / 120) * MouseVolumeStep;
        }

        _pendingVolumeChange += increment;

        if (Math.Abs(_pendingVolumeChange) < VolumeChangeThreshold)
            return;

        using var device = AudioDeviceMonitor.Instance.GetDefaultRenderDevice();
        if (device == null)
        {
            _pendingVolumeChange = 0f;
            return;
        }

        float current = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        float target = Math.Clamp(current + _pendingVolumeChange, 0f, 1f);
        _pendingVolumeChange = 0f;

        if (Math.Abs(target - current) <= float.Epsilon)
            return;

        bool goingUp = target > current;

        // Pre-offset: set to (target minus one OS step) so the async keybd_event press
        // lands the volume exactly on target. Sending a key on every successful change
        // also resets the OSD auto-dismiss timer, keeping it visible throughout scrolling.
        float preVolume = goingUp
            ? Math.Clamp(target - OsVolumeStep, 0f, 1f)
            : Math.Clamp(target + OsVolumeStep, 0f, 1f);

        device.AudioEndpointVolume.MasterVolumeLevelScalar = preVolume;

        byte vk = goingUp ? (byte)0xAF : (byte)0xAE; // VK_VOLUME_UP : VK_VOLUME_DOWN
        var sentinel = new IntPtr(NativeMethods.VolumeOsdExtraInfo);
        NativeMethods.keybd_event(vk, 0, 0, sentinel);
        NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYUP, sentinel);

        e.Handled = true;
    }
}
