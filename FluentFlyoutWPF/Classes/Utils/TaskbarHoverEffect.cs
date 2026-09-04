// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluentFlyoutWPF.Classes.Utils;

internal static class TaskbarHoverEffect
{
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(200));

    public static void Apply(Border mainBorder, Border topBorder)
    {
        // hover effects with animations, hard-coded colors because I can't find the resource brushes
        WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
        bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;

        var background = isDark
            ? new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 }
            : new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) { Opacity = 0.6 };

        topBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = isDark ? 0.25 : 1 };

        // rare case where background is not a SolidColorBrush after SetupWindow
        if (mainBorder.Background is not SolidColorBrush)
        {
            mainBorder.Background = new SolidColorBrush(Colors.Transparent) { Opacity = 0 };
        }

        Animate(mainBorder, background.Color, background.Opacity, EasingMode.EaseOut);
    }

    public static void Clear(Border mainBorder, Border topBorder)
    {
        Animate(mainBorder, Colors.Transparent, 0, EasingMode.EaseInOut);
        topBorder.BorderBrush = Brushes.Transparent;
    }

    private static void Animate(Border mainBorder, Color color, double opacity, EasingMode easingMode)
    {
        var easing = new CubicEase { EasingMode = easingMode };

        mainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            To = color,
            Duration = AnimationDuration,
            EasingFunction = easing
        });

        mainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation
        {
            To = opacity,
            Duration = AnimationDuration,
            EasingFunction = easing
        });
    }
}