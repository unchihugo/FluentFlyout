// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Media.Control;
using Wpf.Ui.Controls;

namespace FluentFlyout.Controls;

/// <summary>
/// Interaction logic for TaskbarWidgetControl.xaml
/// </summary>
public partial class TaskbarWidgetControl : UserControl
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // Constants for default and small taskbar widget sizes
    private const double DefaultTaskbarWidgetHeight = 40;
    private const double SmallTaskbarWidgetHeight = 28;

    // Constants for cover image and control button sizes
    private const double DefaultCoverImageSize = 36;
    private const double SmallCoverImageSize = 24;
    private const double DefaultCoverImageMargin = 55;
    private const double SmallCoverImageMargin = 43;
    private const double DefaultPlaceholderIconSize = 24;
    private const double SmallPlaceholderIconSize = 18;
    private const double DefaultMediaPlayerIconSize = 12;
    private const double SmallMediaPlayerIconSize = 10;
    private const double DefaultMediaPlayerCutoutSize = 18;
    private const double SmallMediaPlayerCutoutSize = 16;
    private const double MediaPlayerIconOffset = 2;
    private const double DefaultControlButtonSize = 32;
    private const double SmallControlButtonSize = 24;
    private const float TaskbarVolumeStep = 0.02f;

    private readonly double _scale = 0.9;
    private readonly int _nativeWidgetsPadding = 216;

    // Cached width calculations
    private string _cachedTitleText = string.Empty;
    private string _cachedArtistText = string.Empty;
    private double _cachedTitleWidth = 0;
    private double _cachedArtistWidth = 0;
    private double _cachedTitleContainerWidth = -1;
    private double _cachedArtistContainerWidth = -1;
    private readonly int _extraMarginForText = 6; // additional margin to avoid text clipping

    private double _cachedTitleOpacityMaskWidth = -1;
    private double _cachedArtistOpacityMaskWidth = -1;
    private LinearGradientBrush? _cachedTitleOpacityMask;
    private LinearGradientBrush? _cachedArtistOpacityMask;

    private string _actualTitle = string.Empty;
    private string _actualArtist = string.Empty;
    private string _songInfoTooltip = string.Empty;
    private float? _appVolume;

    // reference to main window for flyout functions
    private MainWindow? _mainWindow;
    private bool _isPaused;
    private bool _isVertical;
    private bool _isTaskbarOnLeft;
    private bool _isSmallTaskbar;
    private readonly DispatcherTimer _seekbarTimer;
    private TimeSpan _timelinePosition;
    private TimeSpan _timelineDuration;
    private DateTimeOffset _timelineUpdatedAt;
    private bool _timelineIsPlaying;
    private int _lastSeekbarMode = -1;

    private readonly FantCover _songCover;

    public TaskbarWidgetControl()
    {
        InitializeComponent();
        _songCover = new FantCover(SongImageBorder, SongImage, _scale);

        // Apply Windows theme colors (independent of the app theme setting)
        ApplyWindowsTheme();

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

        MainBorder.SizeChanged += (s, e) =>
        {
            var rect = new RectangleGeometry(new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight), 6, 6);
            MainBorder.Clip = rect;
            RefreshSeekbarProgress();
        };

        SongImageBorder.SizeChanged += (_, _) => RefreshSeekbarProgress();

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

        _seekbarTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _seekbarTimer.Tick += (_, _) => RefreshSeekbarProgress();

        // Initialize control order
        ReorderControls();
    }

    public void ReorderControls()
    {
        // Remove ControlsStackPanel from MainStackPanel
        MainStackPanel.Children.Remove(ControlsStackPanel);

        // Reorder based on position setting
        if (SettingsManager.Current.TaskbarWidgetControlsPosition == 0)
        {
            // Left: Controls, Image, Info
            MainStackPanel.Children.Insert(0, ControlsStackPanel);
            ControlsStackPanel.Margin = new Thickness(2, 0, 6, 0); // for some reason margins are weird on left side
        }
        else
        {
            // Right: Image, Info, Controls
            MainStackPanel.Children.Add(ControlsStackPanel);
            ControlsStackPanel.Margin = new Thickness(8, 0, 0, 0);
        }
    }

    public void SetVerticalMode(bool isVertical, bool isTaskbarOnLeft = true)
    {
        _isVertical = isVertical;
        _isTaskbarOnLeft = isTaskbarOnLeft;
        SongInfoStackPanel.Visibility = isVertical ? Visibility.Collapsed : Visibility.Visible;
        SongArtistContainer.Visibility = !_isSmallTaskbar && !isVertical && !string.IsNullOrEmpty(_actualArtist)
            ? Visibility.Visible
            : Visibility.Collapsed;

        var counterRotate = isVertical ? new RotateTransform(-90) : null;

        SongImageBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        SongImageBorder.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;

        foreach (var button in new Wpf.Ui.Controls.Button[] { PreviousButton, PlayPauseButton, NextButton })
        {
            button.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            button.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;
        }

        // The widget itself is rotated by TaskbarWindow on vertical taskbars.
        // Its local bottom edge becomes the visual left edge after rotation;
        // use the local top edge on a right-side taskbar so the seekbar appears on the right.
        BottomProgressContainer.VerticalAlignment = !isVertical || isTaskbarOnLeft
            ? VerticalAlignment.Bottom
            : VerticalAlignment.Top;

        UpdateSeekbarPresentation();
    }

    public void SetSmallTaskbarMode(bool isSmallTaskbar)
    {
        _isSmallTaskbar = isSmallTaskbar;
        SongArtistContainer.Visibility = !isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
            ? Visibility.Visible
            : Visibility.Collapsed;

        double coverImageSize = isSmallTaskbar ? SmallCoverImageSize : DefaultCoverImageSize;
        SongImageBorder.Width = coverImageSize;
        SongImageBorder.Height = coverImageSize;
        SongImagePlaceholder.FontSize = isSmallTaskbar ? SmallPlaceholderIconSize : DefaultPlaceholderIconSize;
        double mediaPlayerIconSize = isSmallTaskbar ? SmallMediaPlayerIconSize : DefaultMediaPlayerIconSize;
        MediaPlayerIconImage.Width = mediaPlayerIconSize;
        MediaPlayerIconImage.Height = mediaPlayerIconSize;
        MediaPlayerIconImage.Margin = new Thickness(0, 0, -MediaPlayerIconOffset, -MediaPlayerIconOffset);
        MediaPlayerIconImage.Clip = new EllipseGeometry(
            new System.Windows.Point(mediaPlayerIconSize / 2, mediaPlayerIconSize / 2),
            mediaPlayerIconSize / 2,
            mediaPlayerIconSize / 2);
        UpdateAlbumArtCutoutMask(MediaPlayerIconImage.Visibility == Visibility.Visible);

        double controlButtonSize = isSmallTaskbar ? SmallControlButtonSize : DefaultControlButtonSize;
        foreach (var button in new Wpf.Ui.Controls.Button[] { PreviousButton, PlayPauseButton, NextButton })
        {
            button.Width = controlButtonSize;
            button.Height = controlButtonSize;
        }

        CoverProgressRing.Width = coverImageSize;
        CoverProgressRing.Height = coverImageSize;
        UpdateSeekbarPresentation();
    }

    private void UpdateAlbumArtCutoutMask(bool showCutout)
    {
        double coverImageSize = _isSmallTaskbar ? SmallCoverImageSize : DefaultCoverImageSize;
        Geometry maskGeometry = new RectangleGeometry(new Rect(0, 0, coverImageSize, coverImageSize), 5, 5);
        if (showCutout)
        {
            double mediaPlayerIconSize = _isSmallTaskbar ? SmallMediaPlayerIconSize : DefaultMediaPlayerIconSize;
            double cutoutSize = _isSmallTaskbar ? SmallMediaPlayerCutoutSize : DefaultMediaPlayerCutoutSize;
            double cutoutCenter = coverImageSize - mediaPlayerIconSize / 2 + MediaPlayerIconOffset;
            var cutoutGeometry = new EllipseGeometry(
                new System.Windows.Point(cutoutCenter, cutoutCenter),
                cutoutSize / 2,
                cutoutSize / 2);
            maskGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, maskGeometry, cutoutGeometry);
        }

        var mask = new DrawingBrush(new GeometryDrawing(Brushes.White, null, maskGeometry))
        {
            Stretch = Stretch.Fill,
            Viewbox = new Rect(0, 0, coverImageSize, coverImageSize),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1, 1),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox
        };
        mask.Freeze();
        AlbumArtBorder.OpacityMask = mask;
    }

    public void SetMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void UpdatePlaybackProgress(TimeSpan position, TimeSpan duration, DateTimeOffset lastUpdatedAt, bool isPlaying)
    {
        Dispatcher.Invoke(() =>
        {
            _timelinePosition = position < TimeSpan.Zero ? TimeSpan.Zero : position;
            _timelineDuration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
            _timelineUpdatedAt = lastUpdatedAt;
            _timelineIsPlaying = isPlaying;
            if (_timelineDuration > TimeSpan.Zero && _timelineIsPlaying)
            {
                if (!_seekbarTimer.IsEnabled)
                    _seekbarTimer.Start();
            }
            else
            {
                _seekbarTimer.Stop();
            }

            UpdateSeekbarPresentation();
            RefreshSeekbarProgress();
        });
    }

    public void StopPlaybackProgress()
    {
        if (Dispatcher.CheckAccess())
            _seekbarTimer.Stop();
        else
            Dispatcher.BeginInvoke(() => _seekbarTimer.Stop());
    }

    private void UpdateSeekbarPresentation()
    {
        int mode = Math.Clamp(SettingsManager.Current.TaskbarWidgetSeekbarMode, 0, 3);
        bool hasProgress = _timelineDuration > TimeSpan.Zero;
        bool presentationChanged = mode != _lastSeekbarMode;
        _lastSeekbarMode = mode;
        MainStackPanel.Margin = new Thickness(4, mode == 0 ? -2 : 0, -100, 0);

        SongArtistContainer.Visibility = !_isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
            ? Visibility.Visible
            : Visibility.Collapsed;
        BottomProgressContainer.Visibility = (mode == 0 || mode == 1) && hasProgress ? Visibility.Visible : Visibility.Collapsed;
        CoverProgressRing.Visibility = mode == 2 && hasProgress ? Visibility.Visible : Visibility.Collapsed;

        if (presentationChanged)
            UpdateMarquees();
    }

    private void RefreshSeekbarProgress()
    {
        if (_timelineDuration <= TimeSpan.Zero)
        {
            UpdateProgressVisuals(0);
            return;
        }

        TimeSpan position = _timelinePosition;
        if (_timelineIsPlaying && _timelineUpdatedAt != default)
            position += DateTimeOffset.Now - _timelineUpdatedAt;

        double ratio = Math.Clamp(position.TotalSeconds / _timelineDuration.TotalSeconds, 0, 1);

        UpdateProgressVisuals(ratio);
    }

    private void UpdateProgressVisuals(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);

        double bottomWidth = MainBorder.ActualWidth;
        if (bottomWidth > 0)
            BottomProgressFill.Width = bottomWidth * ratio;

        UpdateCoverProgressRing(ratio);
    }

    private void UpdateCoverProgressRing(double ratio)
    {
        double size = SongImageBorder.ActualWidth;
        if (size <= 0 || ratio <= 0)
        {
            CoverProgressRing.Data = Geometry.Empty;
            return;
        }

        // Album artwork is square, so the progress indicator follows its rounded-square outline.
        double stroke = CoverProgressRing.StrokeThickness;
        double inset = stroke / 2;
        double edge = size - inset;
        double cornerRadius = Math.Max(0, Math.Min(5, size / 2) - inset);
        double straight = Math.Max(0, edge - inset - 2 * cornerRadius);
        double arcLength = Math.PI * cornerRadius / 2;
        double perimeter = straight * 4 + arcLength * 4;
        if (perimeter <= 0)
            return;

        var points = new List<Point>();
        void AddLine(Point start, Point end, double length)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(length / 1.5));
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                points.Add(new Point(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t));
            }
        }
        void AddCorner(Point center, double startAngle)
        {
            int steps = Math.Max(4, (int)Math.Ceiling(90 * cornerRadius / 1.5));
            for (int i = 0; i <= steps; i++)
            {
                double angle = (startAngle + 90.0 * i / steps) * Math.PI / 180;
                points.Add(new Point(center.X + cornerRadius * Math.Cos(angle), center.Y + cornerRadius * Math.Sin(angle)));
            }
        }

        double left = inset;
        double top = inset;
        double right = edge;
        double bottom = edge;
        AddLine(new Point(left + cornerRadius, top), new Point(right - cornerRadius, top), straight);
        AddCorner(new Point(right - cornerRadius, top + cornerRadius), -90);
        AddLine(new Point(right, top + cornerRadius), new Point(right, bottom - cornerRadius), straight);
        AddCorner(new Point(right - cornerRadius, bottom - cornerRadius), 0);
        AddLine(new Point(right - cornerRadius, bottom), new Point(left + cornerRadius, bottom), straight);
        AddCorner(new Point(left + cornerRadius, bottom - cornerRadius), 90);
        AddLine(new Point(left, bottom - cornerRadius), new Point(left, top + cornerRadius), straight);
        AddCorner(new Point(left + cornerRadius, top + cornerRadius), 180);

        var sampled = new List<Point>();
        double targetLength = perimeter * ratio;
        double travelled = 0;
        Point previous = points[0];
        sampled.Add(previous);
        for (int i = 1; i < points.Count && travelled < targetLength; i++)
        {
            Point next = points[i];
            double segmentLength = (next - previous).Length;
            if (travelled + segmentLength >= targetLength)
            {
                double t = segmentLength <= 0 ? 0 : (targetLength - travelled) / segmentLength;
                sampled.Add(new Point(previous.X + (next.X - previous.X) * t, previous.Y + (next.Y - previous.Y) * t));
                break;
            }

            travelled += segmentLength;
            sampled.Add(next);
            previous = next;
        }

        if (ratio >= 0.9999)
            sampled.Add(points[0]);

        PathFigure figure = new() { StartPoint = sampled[0], IsClosed = false };
        if (sampled.Count > 1)
            figure.Segments.Add(new PolyLineSegment(sampled.Skip(1), true));
        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        CoverProgressRing.Data = geometry;
    }

    public void UpdateCoverProgressColor()
    {
        if (SettingsManager.Current.UseAlbumArtAsAccentColor && BitmapHelper.SavedDominantColors.Count > 0)
        {
            SolidColorBrush brush = BitmapHelper.SavedDominantColors.Last();
            BottomProgressFill.Background = brush;
            CoverProgressRing.Stroke = brush;
        }
        else
        {
            // Restore the XAML DynamicResource values so Windows accent changes keep flowing through.
            BottomProgressFill.ClearValue(Border.BackgroundProperty);
            CoverProgressRing.ClearValue(Shape.StrokeProperty);
        }
    }

    public void ApplyWindowsTheme()
    {
        WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
        bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;

        var foreground = new SolidColorBrush(isDark
            ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0xE4, 0x1C, 0x1C, 0x1C));

        SongTitle.Foreground = foreground;
        SongArtist.Foreground = foreground;
        var progressTrackBrush = new SolidColorBrush(Color.FromArgb(0x40,
            isDark ? (byte)255 : (byte)0,
            isDark ? (byte)255 : (byte)0,
            isDark ? (byte)255 : (byte)0));
        BottomProgressTrack.Background = progressTrackBrush;
        PreviousButton.Foreground = foreground;
        PlayPauseButton.Foreground = foreground;
        NextButton.Foreground = foreground;
    }

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

        RefreshAppVolumeTooltip();

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
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

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

        TopBorder.BorderBrush = Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mainWindow == null) return;

        // toggle main flyout when clicked
        _mainWindow.ShowMediaFlyout(toggleMode: true, forceShow: true, withVolume: true);
    }

    private void MainBorder_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SettingsManager.Current.TaskbarWidgetScrollVolumeMode != 0 && _mainWindow != null)
        {
            float volumeDelta = Math.Clamp(e.Delta / 120f * TaskbarVolumeStep, -1f, 1f);
            _mainWindow.AdjustTaskbarVolume(volumeDelta);
        }

        e.Handled = true;
    }

    public (double logicalWidth, double logicalHeight) CalculateSize(double dpiScale)
    {
        double coverImageMargin = _isSmallTaskbar ? SmallCoverImageMargin : DefaultCoverImageMargin;

        // calculate widget width - use cached values if text hasn't changed
        string currentTitle = _actualTitle;
        string currentArtist = _actualArtist;

        bool textChanged = false;

        if (!string.Equals(currentTitle, _cachedTitleText, StringComparison.Ordinal))
        {
            _cachedTitleWidth = Math.Round(StringWidth.GetStringWidth(currentTitle, 400), 2);
            _cachedTitleText = currentTitle;
            textChanged = true;
        }
        if (!string.Equals(currentArtist, _cachedArtistText, StringComparison.Ordinal))
        {
            _cachedArtistWidth = Math.Round(StringWidth.GetStringWidth(currentArtist, 400), 2);
            _cachedArtistText = currentArtist;
            textChanged = true;
        }

        // maximum width limit, same as Windows native widget
        double maxLogicalWidth = _nativeWidgetsPadding / _scale;
        double logicalWidth;

        if (_isVertical)
        {
            logicalWidth = coverImageMargin;
        }
        else if (SettingsManager.Current.TaskbarWidgetFixedWidth)
        {
            // pin to maximum width so right-aligned controls don't shift between songs
            logicalWidth = maxLogicalWidth;
        }
        else
        {
            double contentWidth = _isSmallTaskbar ? _cachedTitleWidth : Math.Max(_cachedTitleWidth, _cachedArtistWidth);
            logicalWidth = contentWidth + coverImageMargin + _extraMarginForText; // add margin for cover image
            logicalWidth = Math.Min(logicalWidth, maxLogicalWidth);
        }

        double newTitleContainerWidth = Math.Max(logicalWidth - coverImageMargin, 0);
        double newArtistContainerWidth = Math.Max(logicalWidth - coverImageMargin, 0);
        bool widthChanged = false;

        if (_cachedTitleContainerWidth != newTitleContainerWidth)
        {
            SongTitleContainer.Width = newTitleContainerWidth;
            _cachedTitleContainerWidth = newTitleContainerWidth;
            widthChanged = true;
        }

        if (_cachedArtistContainerWidth != newArtistContainerWidth)
        {
            SongArtistContainer.Width = newArtistContainerWidth;
            _cachedArtistContainerWidth = newArtistContainerWidth;
            widthChanged = true;
        }

        // Refresh animations if layout bounds or text contents change
        if (textChanged || widthChanged)
        {
            UpdateMarquees();
        }

        // add space for playback controls if enabled and visible
        if (SettingsManager.Current.TaskbarWidgetControlsEnabled && ControlsStackPanel.Visibility == Visibility.Visible)
        {
            double controlsWidth = PreviousButton.Width + PlayPauseButton.Width + NextButton.Width;
            if (!_isVertical)
                controlsWidth += ControlsStackPanel.Margin.Left + ControlsStackPanel.Margin.Right;

            logicalWidth += controlsWidth;
        }

        double logicalHeight = _isSmallTaskbar ? SmallTaskbarWidgetHeight : DefaultTaskbarWidgetHeight;

        return (logicalWidth, logicalHeight);
    }

    public void UpdateMarquees()
    {
        double titleAvailableWidth = double.IsNaN(SongTitleContainer.Width) ? 0 : SongTitleContainer.Width;
        double artistAvailableWidth = double.IsNaN(SongArtistContainer.Width) ? 0 : SongArtistContainer.Width;

        bool isScrollingEnabled = SettingsManager.Current.TaskbarWidgetScrollingEnabled;

        UpdateMarquee(SongTitle, SongTitleContainer, _cachedTitleWidth, titleAvailableWidth, isScrollingEnabled);
        UpdateMarquee(SongArtist, SongArtistContainer, _cachedArtistWidth, artistAvailableWidth, isScrollingEnabled);
    }

    private void UpdateMarquee(System.Windows.Controls.TextBlock textBlock, Canvas container, double textWidth, double availableWidth, bool isEnabled)
    {
        if (textBlock.RenderTransform as TranslateTransform is not { } transform) return;

        int speed = SettingsManager.Current.TaskbarWidgetScrollingTextSpeed;
        bool loopForever = SettingsManager.Current.TaskbarWidgetScrollingTextLoopForever;
        bool isTitle = textBlock == SongTitle;
        double containerWidth = container.Width;

        // references moved outside so they may be called in the else block later
        ref double cachedMaskWidth = ref (isTitle ? ref _cachedTitleOpacityMaskWidth : ref _cachedArtistOpacityMaskWidth);
        ref LinearGradientBrush? cachedMask = ref (isTitle ? ref _cachedTitleOpacityMask : ref _cachedArtistOpacityMask);

        if (isEnabled && textWidth > availableWidth && containerWidth > 0 && !double.IsNaN(containerWidth))
        {
            textBlock.Width = double.NaN;
            textBlock.TextTrimming = TextTrimming.None;

            string origText = isTitle ? _actualTitle : _actualArtist;

            if (cachedMask == null || Math.Abs(containerWidth - cachedMaskWidth) > 0.5)
            {
                // 12.0 is the width in pixels of the gradient fade on the left and right hand edges of the 
                // text container.
                double fadeFraction = 12.0 / containerWidth;
                if (fadeFraction > 0.5) fadeFraction = 0.5;

                cachedMask = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(containerWidth, 0),
                    MappingMode = BrushMappingMode.Absolute
                };

                cachedMask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0));
                cachedMask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), fadeFraction));
                cachedMask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0 - fadeFraction));
                cachedMask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
                cachedMaskWidth = containerWidth;
            }

            container.OpacityMask = cachedMask;

            if (loopForever)
            {
                // continuous looping should have the fades constantly active (as its infinite)
                cachedMask.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, null);
                cachedMask.GradientStops[3].BeginAnimation(GradientStop.ColorProperty, null);
                cachedMask.GradientStops[0].Color = Color.FromArgb(0, 255, 255, 255);
                cachedMask.GradientStops[3].Color = Color.FromArgb(0, 255, 255, 255);

                // \u00A0 are non-breaking spaces, which prevents WPF from collapsing and/or trimming
                // them
                string spacer = "\u00A0\u00A0\u00A0\u00A0\u00A0";
                textBlock.Text = origText + spacer + origText;

                double spacerWidth = StringWidth.GetStringWidth(spacer, 400);
                double scrollDistance = textWidth + spacerWidth;

                double durationToScroll = scrollDistance / speed;
                var animation = new DoubleAnimation
                {
                    From = 0,
                    To = -scrollDistance,
                    Duration = TimeSpan.FromSeconds(durationToScroll),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                transform.BeginAnimation(TranslateTransform.XProperty, animation);
            }
            else
            {
                // Adding 10 pixels gives extra padding so the text scrolls past the container's edge before
                // resetting or reversing; this prevents abrupt cutoffs
                double scrollDistance = textWidth - containerWidth + 10;
                textBlock.Text = origText;

                double durationSeconds = scrollDistance / speed;
                double pauseDuration = 2.0; // wait 2 seconds at the start and end of the scroll
                double tWaitStart = pauseDuration;
                double tScrollEnd = tWaitStart + durationSeconds;
                double tWaitEnd = tScrollEnd + pauseDuration;
                double tScrollBackEnd = tWaitEnd + durationSeconds;
                double tTotalCycle = tScrollBackEnd + pauseDuration;

                var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitStart))));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollEnd))));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitEnd))));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollBackEnd))));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tTotalCycle))));

                // sync fades with the "ping pong" movement
                Color transparentWhite = Color.FromArgb(0, 255, 255, 255);
                Color solidWhite = Color.FromArgb(255, 255, 255, 255);

                // 300 ms is the capped duration for the fade transition; we clamp it so that the fade animation
                // doesn't overlap with the scroll animation on certain shorter texts
                TimeSpan fadeTime = TimeSpan.FromMilliseconds(Math.Min(300, durationSeconds * 1000 / 2.0));

                var leftColorAnim = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                leftColorAnim.KeyFrames.Add(new DiscreteColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                leftColorAnim.KeyFrames.Add(new LinearColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitStart))));
                leftColorAnim.KeyFrames.Add(new LinearColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitStart) + fadeTime)));
                leftColorAnim.KeyFrames.Add(new LinearColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollBackEnd) - fadeTime)));
                leftColorAnim.KeyFrames.Add(new LinearColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollBackEnd))));
                leftColorAnim.KeyFrames.Add(new LinearColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tTotalCycle))));

                var rightColorAnim = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                rightColorAnim.KeyFrames.Add(new DiscreteColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                rightColorAnim.KeyFrames.Add(new LinearColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollEnd) - fadeTime)));
                rightColorAnim.KeyFrames.Add(new LinearColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollEnd))));
                rightColorAnim.KeyFrames.Add(new LinearColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitEnd))));
                rightColorAnim.KeyFrames.Add(new LinearColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitEnd) + fadeTime)));
                rightColorAnim.KeyFrames.Add(new LinearColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tTotalCycle))));

                cachedMask.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, leftColorAnim);
                cachedMask.GradientStops[3].BeginAnimation(GradientStop.ColorProperty, rightColorAnim);

                transform.BeginAnimation(TranslateTransform.XProperty, animation);
            }
        }
        else
        {
            if (cachedMask != null)
            {
                // Prevent memory leaks and/or unwanted behavior by clearing the color animations when the mask is hidden
                cachedMask.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, null);
                cachedMask.GradientStops[3].BeginAnimation(GradientStop.ColorProperty, null);
            }

            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
            textBlock.Text = isTitle ? _actualTitle : _actualArtist;
            textBlock.Width = containerWidth;
            textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            container.OpacityMask = null;
        }
    }

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null, ImageSource? mediaPlayerIcon = null)
    {
        if (title == "-" && artist == "-")
        {
            // No media playing, hide UI
            Dispatcher.Invoke(() =>
            {
                _actualTitle = string.Empty;
                _actualArtist = string.Empty;
                _songInfoTooltip = string.Empty;
                _appVolume = null;
                _timelinePosition = TimeSpan.Zero;
                _timelineDuration = TimeSpan.Zero;
                _timelineIsPlaying = false;
                _seekbarTimer.Stop();
                MediaPlayerIconImage.Source = null;
                MediaPlayerIconImage.Visibility = Visibility.Collapsed;
                UpdateAlbumArtCutoutMask(false);

                if (SettingsManager.Current.TaskbarWidgetHideCompletely)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                ControlsStackPanel.Visibility = Visibility.Collapsed;
                SongTitle.Text = string.Empty;
                SongArtist.Text = string.Empty;
                SongInfoStackPanel.Visibility = Visibility.Collapsed;
                SongInfoStackPanel.ToolTip = string.Empty;
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                _songCover.Source = null;
                BackgroundImage.Source = null;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
                TopBorder.BorderBrush = Brushes.Transparent;
                UpdateSeekbarPresentation();

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
            string newTitle = !string.IsNullOrEmpty(title) ? title : "-";
            string newArtist = artist ?? string.Empty;

            if (_actualTitle != newTitle || _actualArtist != newArtist)
            {
                // changed info
                if (SettingsManager.Current.TaskbarWidgetAnimated)
                {
                    AnimateEntrance();
                }

                _actualTitle = newTitle;
                _actualArtist = newArtist;

                _timelinePosition = TimeSpan.Zero;
                _timelineDuration = TimeSpan.Zero;
                SongTitle.Text = _actualTitle;
                SongArtist.Text = _actualArtist;
            }

            // Update tooltip with song info and the active app volume
            _songInfoTooltip = string.Empty;
            _songInfoTooltip += !string.IsNullOrEmpty(title) ? title : string.Empty;
            _songInfoTooltip += !string.IsNullOrEmpty(artist) ? "\n\n" + artist : string.Empty;
            _appVolume = _mainWindow?.GetActiveMediaAppVolume();
            UpdateSongInfoTooltip();

            if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
            {
                PlayPauseButton.Icon = _isPaused ? new SymbolIcon(SymbolRegular.Play24, filled: true) : new SymbolIcon(SymbolRegular.Pause24, filled: true);
            }

            // change color of icon
            SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0 ?
                BitmapHelper.SavedDominantColors.Last()
                : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
            SongImagePlaceholder.Foreground = brush;
            UpdateCoverProgressColor();

            if (icon != null)
            {
                if (_isPaused && SettingsManager.Current.TaskbarWidgetShowPauseOverlay)
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
                _songCover.Source = icon;
                BackgroundImage.Source = icon;
                SongImageBorder.Margin = new Thickness(0, 0, 0, 0); // align image better when cover is present
            }
            else
            {
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                _songCover.Source = null;
                BackgroundImage.Source = null;
            }

            bool showMediaPlayerIcon = SettingsManager.Current.TaskbarWidgetShowMediaPlayerIcon
                && icon != null
                && mediaPlayerIcon != null;
            MediaPlayerIconImage.Source = showMediaPlayerIcon ? mediaPlayerIcon : null;
            MediaPlayerIconImage.Visibility = showMediaPlayerIcon ? Visibility.Visible : Visibility.Collapsed;
            UpdateAlbumArtCutoutMask(showMediaPlayerIcon);

            SongTitle.Visibility = Visibility.Visible;
            SongArtistContainer.Visibility = !_isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
                ? Visibility.Visible
                : Visibility.Collapsed;
            SongInfoStackPanel.Visibility = _isVertical ? Visibility.Collapsed : Visibility.Visible;
            BackgroundImage.Visibility = SettingsManager.Current.TaskbarWidgetBackgroundBlur ? Visibility.Visible : Visibility.Collapsed;

            // on top of XAML visibility binding (XAML binding only hides when disabled in settings)
            ControlsStackPanel.Visibility = SettingsManager.Current.TaskbarWidgetControlsEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;

            UpdateSeekbarPresentation();

            Visibility = Visibility.Visible;
        });
    }

    public void RefreshAppVolumeTooltip()
    {
        Dispatcher.Invoke(() =>
        {
            _appVolume = _mainWindow?.GetActiveMediaAppVolume();
            UpdateSongInfoTooltip();
        });
    }

    private void UpdateSongInfoTooltip()
    {
        SongInfoStackPanel.ToolTip = _songInfoTooltip;
        if (_appVolume is float appVolume)
            SongInfoStackPanel.ToolTip += $" ({appVolume:P0})";
    }

    private async void AnimateEntrance()
    {
        try
        {
            int msDuration = MainWindow.getDuration();

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

    // event handlers for media control buttons
    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;
        await _mainWindow.TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;
        await _mainWindow.TryTogglePlayPauseAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;
        await _mainWindow.TrySkipNextAsync();
    }

    // Event handlers for context menu items
    private async void ContextMenuMediaPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        _ = _mainWindow.TryOpenMediaPlayerAsync();
    }

    private void TaskbarContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _mainWindow?.PopulateSessionSwitcherMenu(ContextMenuSessionSwitcher);
    }

    private void ContextMenuSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsWindow.ShowInstance();
    }
}