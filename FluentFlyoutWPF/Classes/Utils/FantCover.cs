// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentFlyout.Classes.Utils;

/// <summary>
/// Shows album art in an <see cref="ImageBrush"/> at the exact pixel size its element is drawn at,
/// shrunk once with WIC's Fant filter (as Windows' own media controls do) instead of being scaled by WPF when drawn.
/// Follows the element's size and the window's DPI.
/// </summary>
internal sealed class FantCover
{
    private readonly FrameworkElement _frame;
    private readonly ImageBrush _brush;
    private readonly double _renderScale;
    private Window? _window;
    private BitmapImage? _source;

    /// <param name="frame">The element the brush fills</param>
    /// <param name="brush">The brush that shows the cover</param>
    /// <param name="renderScale">Any render transform scale applied above the element (e.g. 0.9 in the taskbar widget)</param>
    public FantCover(FrameworkElement frame, ImageBrush brush, double renderScale = 1.0)
    {
        _frame = frame;
        _brush = brush;
        _renderScale = renderScale;

        _frame.SizeChanged += (_, _) => Apply();
        _frame.Loaded += (_, _) =>
        {
            var window = Window.GetWindow(_frame);
            if (window != _window)
            {
                if (_window != null)
                    _window.DpiChanged -= Window_DpiChanged;
                _window = window;
                if (_window != null)
                    _window.DpiChanged += Window_DpiChanged;
            }
            Apply();
        };
    }

    /// <summary>
    /// The full-size thumbnail from <see cref="BitmapHelper.GetThumbnail"/>, or null for no cover
    /// </summary>
    public BitmapImage? Source
    {
        get => _source;
        set
        {
            _source = value;
            Apply();
        }
    }

    private void Window_DpiChanged(object sender, DpiChangedEventArgs e) => Apply();

    private void Apply()
    {
        if (_source == null)
        {
            _brush.ImageSource = null;
            return;
        }

        double width = _frame.ActualWidth > 0 ? _frame.ActualWidth : _frame.Width;
        double height = _frame.ActualHeight > 0 ? _frame.ActualHeight : _frame.Height;
        if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0)
        {
            // not laid out yet; SizeChanged will apply the sized cover
            _brush.ImageSource = _source;
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(_frame);
        int pixelWidth = (int)Math.Round(width * dpi.DpiScaleX * _renderScale);
        int pixelHeight = (int)Math.Round(height * dpi.DpiScaleY * _renderScale);

        _brush.ImageSource = BitmapHelper.GetCoverForSize(_source, pixelWidth, pixelHeight) ?? _source;
    }
}