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
    // design width of the icon block incl. layout slack (couples with the panel's -100 right margin)
    private const double DefaultCoverImageMargin = 55;
    private const double SmallCoverImageMargin = 43;
    private const double DefaultPlaceholderIconSize = 24;
    private const double SmallPlaceholderIconSize = 18;
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
    private bool _isSmallTaskbar;

    // adaptive layout state (see WidgetLayoutSolver)
    private WidgetLayoutTier _layoutTier = WidgetLayoutTier.Full;
    private bool _layoutControlsShown = true;
    private bool _layoutTextShown = true;
    private double _lastControlsWidth;
    private bool _layoutTextDirty;

    // while a width animation runs, marquee rebuilds are deferred to the final settle pass
    public bool SuppressMarqueeUpdates { get; set; }

    public TaskbarWidgetControl()
    {
        InitializeComponent();

        // Apply Windows theme colors (independent of the app theme setting)
        ApplyWindowsTheme();

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

        MainBorder.SizeChanged += (s, e) =>
        {
            var rect = new RectangleGeometry(new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight), 6, 6);
            MainBorder.Clip = rect;
        };

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

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

    public void SetVerticalMode(bool isVertical)
    {
        _isVertical = isVertical;
        SongInfoStackPanel.Visibility = ResolveSongInfoVisibility();
        SongArtistContainer.Visibility = ResolveArtistVisibility();

        var counterRotate = isVertical ? new RotateTransform(-90) : null;

        SongImageBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        SongImageBorder.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;

        foreach (var button in new Wpf.Ui.Controls.Button[] { PreviousButton, PlayPauseButton, NextButton })
        {
            button.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            button.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;
        }
    }

    public void SetSmallTaskbarMode(bool isSmallTaskbar)
    {
        _isSmallTaskbar = isSmallTaskbar;
        SongArtistContainer.Visibility = ResolveArtistVisibility();

        double coverImageSize = isSmallTaskbar ? SmallCoverImageSize : DefaultCoverImageSize;
        SongImageBorder.Width = coverImageSize;
        SongImageBorder.Height = coverImageSize;
        SongImagePlaceholder.FontSize = isSmallTaskbar ? SmallPlaceholderIconSize : DefaultPlaceholderIconSize;

        double controlButtonSize = isSmallTaskbar ? SmallControlButtonSize : DefaultControlButtonSize;
        foreach (var button in new Wpf.Ui.Controls.Button[] { PreviousButton, PlayPauseButton, NextButton })
        {
            button.Width = controlButtonSize;
            button.Height = controlButtonSize;
        }
    }

    public void SetMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
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
        PreviousButton.Foreground = foreground;
        PlayPauseButton.Foreground = foreground;
        NextButton.Foreground = foreground;
    }

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

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
        _mainWindow.ShowMediaFlyout(toggleMode: true, forceShow: true);
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

    public (double logicalWidth, double logicalHeight, double visualizerWidth) CalculateSize(double dpiScale, double availableSpan = double.PositiveInfinity)
    {
        var inputs = GetLayoutInputs(availableSpan);
        var layout = WidgetLayoutSolver.Solve(inputs);
        LogAdaptiveLayout(layout, availableSpan);
        double logicalWidth = ApplyLayout(layout);
        double logicalHeight = _isSmallTaskbar ? SmallTaskbarWidgetHeight : DefaultTaskbarWidgetHeight;
        return (logicalWidth, logicalHeight, layout.VisualizerWidth);
    }

    private bool HasMedia => !string.IsNullOrEmpty(_actualTitle) || !string.IsNullOrEmpty(_actualArtist);

    // last adaptive layout state logged; used to rate-limit the diagnostics below
    private WidgetLayoutTier _lastLoggedTier = (WidgetLayoutTier)(-1);
    private bool _lastLoggedTextShown = true;
    private bool _lastLoggedControlsShown = true;
    private bool _lastLoggedVisualizerShown = true;
    private double _lastLoggedWidth = -1;

    private void LogAdaptiveLayout(WidgetLayoutResult layout, double availableSpan)
    {
        if (!SettingsManager.Current.TaskbarWidgetAdaptiveWidth || SuppressMarqueeUpdates)
            return;

        bool visualizerShown = layout.VisualizerWidth > 0;
        bool stateChanged = layout.Tier != _lastLoggedTier
            || layout.TextShown != _lastLoggedTextShown
            || layout.ControlsShown != _lastLoggedControlsShown
            || visualizerShown != _lastLoggedVisualizerShown;

        if (!stateChanged && Math.Abs(layout.WidgetWidth - _lastLoggedWidth) <= 8)
            return;

        Logger.Info($"Adaptive layout: {layout.Tier} width={layout.WidgetWidth:F0} " +
            $"span={(double.IsInfinity(availableSpan) ? -1 : availableSpan):F0} natural={layout.NaturalWidth:F0} " +
            $"text={layout.TextShown} controls={layout.ControlsShown} visualizer={visualizerShown}");

        _lastLoggedTier = layout.Tier;
        _lastLoggedTextShown = layout.TextShown;
        _lastLoggedControlsShown = layout.ControlsShown;
        _lastLoggedVisualizerShown = visualizerShown;
        _lastLoggedWidth = layout.WidgetWidth;
    }

    /// <summary>
    /// Refreshes the cached text width measurements. Returns true when the text changed.
    /// </summary>
    private bool EnsureTextWidthsCached()
    {
        bool textChanged = false;

        if (!string.Equals(_actualTitle, _cachedTitleText, StringComparison.Ordinal))
        {
            _cachedTitleWidth = Math.Round(StringWidth.GetStringWidth(_actualTitle, 400), 2);
            _cachedTitleText = _actualTitle;
            textChanged = true;
        }
        if (!string.Equals(_actualArtist, _cachedArtistText, StringComparison.Ordinal))
        {
            _cachedArtistWidth = Math.Round(StringWidth.GetStringWidth(_actualArtist, 400), 2);
            _cachedArtistText = _actualArtist;
            textChanged = true;
        }

        return textChanged;
    }

    private WidgetLayoutInputs GetLayoutInputs(double availableSpan)
    {
        _layoutTextDirty |= EnsureTextWidthsCached();

        bool controlsVisible = SettingsManager.Current.TaskbarWidgetControlsEnabled && HasMedia;
        double controlsWidth = 0;
        if (controlsVisible)
        {
            controlsWidth = PreviousButton.Width + PlayPauseButton.Width + NextButton.Width;
            if (!_isVertical)
                controlsWidth += ControlsStackPanel.Margin.Left + ControlsStackPanel.Margin.Right;
        }
        _lastControlsWidth = controlsWidth;

        return new WidgetLayoutInputs
        {
            AvailableSpan = availableSpan,
            TitleWidth = _cachedTitleWidth,
            ArtistWidth = _cachedArtistWidth,
            CoverMargin = _isSmallTaskbar ? SmallCoverImageMargin : DefaultCoverImageMargin,
            ControlsWidth = controlsWidth,
            ControlsVisible = controlsVisible,
            IsVertical = _isVertical,
            IsSmallTaskbar = _isSmallTaskbar,
            FixedWidth = SettingsManager.Current.TaskbarWidgetFixedWidth,
            AdaptiveEnabled = SettingsManager.Current.TaskbarWidgetAdaptiveWidth,
            VisualizerEnabled = SettingsManager.Current.TaskbarVisualizerEnabled,
            MaxWidgetWidth = _nativeWidgetsPadding / _scale,
            PriorityOrder = NormalizePriorityOrder(),
        };
    }

    private string[] NormalizePriorityOrder()
    {
        var order = SettingsManager.Current.TaskbarWidgetPriorityOrder
            .Where(WidgetLayoutElementNames.IsKnown)
            .Distinct()
            .ToList();

        foreach (var name in WidgetLayoutElementNames.All)
            if (!order.Contains(name))
                order.Add(name);

        return [.. order];
    }

    /// <summary>
    /// Applies a solved layout: resizes the text containers, refreshes the marquees and updates
    /// which elements are visible for the resolved tier.
    /// </summary>
    private double ApplyLayout(WidgetLayoutResult layout)
    {
        _layoutTier = layout.Tier;
        _layoutControlsShown = layout.ControlsShown;
        _layoutTextShown = layout.TextShown;

        double coverImageMargin = _isSmallTaskbar ? SmallCoverImageMargin : DefaultCoverImageMargin;
        // subtract the controls width only when the solver actually shows them - when the
        // ladder hides the controls, the solved width already excludes them
        double shownControlsWidth = layout.ControlsShown ? _lastControlsWidth : 0;
        double newTextContainerWidth = layout.TextShown
            ? Math.Max(layout.WidgetWidth - shownControlsWidth - coverImageMargin, 0)
            : 0;
        bool widthChanged = false;

        if (_cachedTitleContainerWidth != newTextContainerWidth)
        {
            SongTitleContainer.Width = newTextContainerWidth;
            _cachedTitleContainerWidth = newTextContainerWidth;
            widthChanged = true;
        }

        if (_cachedArtistContainerWidth != newTextContainerWidth)
        {
            SongArtistContainer.Width = newTextContainerWidth;
            _cachedArtistContainerWidth = newTextContainerWidth;
            widthChanged = true;
        }

        // Refresh animations if layout bounds or text contents change. Skipped while the
        // width animation runs - the final settle pass applies the real width and refreshes.
        if ((widthChanged || _layoutTextDirty) && !SuppressMarqueeUpdates)
        {
            UpdateMarquees();
            _layoutTextDirty = false;
        }
        else if (widthChanged)
        {
            _layoutTextDirty = true; // remember to refresh marquees on the settle pass
        }

        ApplyTierVisibility();
        return layout.WidgetWidth;
    }

    /// <summary>
    /// Re-applies element visibilities for the current adaptive layout tier and media state.
    /// </summary>
    private void ApplyTierVisibility()
    {
        SongInfoStackPanel.Visibility = ResolveSongInfoVisibility();
        SongArtistContainer.Visibility = ResolveArtistVisibility();
        ControlsStackPanel.Visibility = ResolveControlsVisibility();
        Visibility = _layoutTier == WidgetLayoutTier.Hidden ? Visibility.Collapsed : Visibility.Visible;
    }

    private Visibility ResolveSongInfoVisibility()
    {
        return !_isVertical && HasMedia && _layoutTextShown && _layoutTier != WidgetLayoutTier.Hidden
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private Visibility ResolveArtistVisibility()
    {
        return !_isSmallTaskbar && !_isVertical && HasMedia && !string.IsNullOrEmpty(_actualArtist)
            && _layoutTextShown && _layoutTier is not (WidgetLayoutTier.Compact or WidgetLayoutTier.Hidden)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private Visibility ResolveControlsVisibility()
    {
        return SettingsManager.Current.TaskbarWidgetControlsEnabled && HasMedia && _layoutControlsShown
            && _layoutTier != WidgetLayoutTier.Hidden
            ? Visibility.Visible
            : Visibility.Collapsed;
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

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
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

                if (SettingsManager.Current.TaskbarWidgetHideCompletely)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                SongTitle.Text = string.Empty;
                SongArtist.Text = string.Empty;
                SongInfoStackPanel.ToolTip = string.Empty;
                ApplyTierVisibility();
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
                TopBorder.BorderBrush = Brushes.Transparent;
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

            // pause overlay follows the pause-icon sub-toggle, music-note placeholder the
            // music-note sub-toggle; both require the master album-art accent switch
            SolidColorBrush brush;
            bool accentMaster = SettingsManager.Current.UseAlbumArtAsAccentColor
                && BitmapHelper.SavedDominantColors.Count > 0;
            bool paused = _isPaused && icon != null;
            if (accentMaster && (paused
                ? SettingsManager.Current.TaskbarWidgetPauseIconUseAccentColor
                : SettingsManager.Current.TaskbarWidgetMusicNoteUseAccentColor))
            {
                brush = BitmapHelper.SavedDominantColors.Last();
            }
            else
            {
                brush = (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
            }
            SongImagePlaceholder.Foreground = brush;

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
                SongImage.ImageSource = icon;
                BackgroundImage.Source = icon;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -2); // align image better when cover is present
            }
            else
            {
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
            }

            SongTitle.Visibility = Visibility.Visible;
            BackgroundImage.Visibility = SettingsManager.Current.TaskbarWidgetBackgroundBlur ? Visibility.Visible : Visibility.Collapsed;

            // element visibilities depend on the adaptive layout tier on top of the XAML bindings
            ApplyTierVisibility();
        });
    }

    /// <summary>
    /// Recolors the icon immediately when an accent sub-toggle changes.
    /// </summary>
    public void ApplyAccentColors()
    {
        Dispatcher.Invoke(() =>
        {
            SolidColorBrush brush;
            bool accentMaster = SettingsManager.Current.UseAlbumArtAsAccentColor
                && BitmapHelper.SavedDominantColors.Count > 0;
            bool paused = _isPaused && SongImage.ImageSource != null;
            if (accentMaster && (paused
                ? SettingsManager.Current.TaskbarWidgetPauseIconUseAccentColor
                : SettingsManager.Current.TaskbarWidgetMusicNoteUseAccentColor))
            {
                brush = BitmapHelper.SavedDominantColors.Last();
            }
            else
            {
                brush = (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
            }
            SongImagePlaceholder.Foreground = brush;
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

    private void ContextMenuSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsWindow.ShowInstance();
    }
}