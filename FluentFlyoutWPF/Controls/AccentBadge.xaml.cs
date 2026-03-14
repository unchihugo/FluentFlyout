// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;

namespace FluentFlyout.Controls;

public partial class AccentBadge : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(AccentBadge),
            new PropertyMetadata("PREMIUM", OnTextChanged));

    public static new readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(AccentBadge),
            new PropertyMetadata(10.0, OnFontSizeChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public new double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((AccentBadge)d).BadgeTextBlock.Text = (string)e.NewValue;
    }

    private static void OnFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var badge = (AccentBadge)d;
        if (badge.BadgeTextBlock is null) return;
        badge.BadgeTextBlock.FontSize = (double)e.NewValue;
    }

    public AccentBadge()
    {
        InitializeComponent();
        BadgeTextBlock.FontSize = FontSize;
    }
}
