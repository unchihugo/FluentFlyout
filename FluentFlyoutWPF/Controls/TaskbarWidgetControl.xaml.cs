// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Helpers;
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

    private readonly double _scale = 0.9;
    private readonly int _nativeWidgetsPadding = 216;

    // Cached width calculations
    private string _cachedTitleText = string.Empty;
    private string _cachedArtistText = string.Empty;
    private double _cachedTitleWidth = 0;
    private double _cachedArtistWidth = 0;
    private double _cachedTitleContainerWidth = -1;
    private double _cachedArtistContainerWidth = -1;
    private bool _lastScrollingTitleSetting = false;
    private bool _lastScrollingArtistSetting = false;

    private int _lastScrollingSpeed = 20;
    private bool _lastScrollingLoop = true;

    private static readonly double _spaceWidth = StringWidth.GetStringWidth("     ", 400);

    private double _cachedTitleOpacityMaskWidth = -1;
    private double _cachedArtistOpacityMaskWidth = -1;
    private LinearGradientBrush? _cachedTitleOpacityMask;
    private LinearGradientBrush? _cachedArtistOpacityMask;

    private double _lastTitleScrollDistance = double.NaN;
    private double _lastArtistScrollDistance = double.NaN;
    private int _lastTitleAnimSpeed = -1;
    private int _lastArtistAnimSpeed = -1;
    private bool _lastTitleLoopForever = false;
    private bool _lastArtistLoopForever = false;

    private string _actualTitle = string.Empty;
    private string _actualArtist = string.Empty;

    // reference to main window for flyout functions
    private MainWindow? _mainWindow;
    private bool _isPaused;

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

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); ;

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
        var counterRotate = isVertical ? new RotateTransform(-90) : null;

        SongImageBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        SongImageBorder.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;

        foreach (var button in new Wpf.Ui.Controls.Button[] { PreviousButton, PlayPauseButton, NextButton })
        {
            button.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            button.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;
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

        TopBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mainWindow == null) return;

        // toggle main flyout when clicked
        _mainWindow.ShowMediaFlyout(toggleMode: true, forceShow: true);
    }

    public (double logicalWidth, double logicalHeight) CalculateSize(double dpiScale)
    {
        // calculate widget width - use cached values if text hasn't changed
        string currentTitle = _actualTitle;
        string currentArtist = _actualArtist;

        bool textChanged = false; // determines if we need to recalculate marquee or just adjust widths

        if (!string.Equals(currentTitle, _cachedTitleText, StringComparison.Ordinal))
        {
            _cachedTitleWidth = StringWidth.GetStringWidth(currentTitle, 400);
            _cachedTitleText = currentTitle;
            textChanged = true;
        }
        if (!string.Equals(currentArtist, _cachedArtistText, StringComparison.Ordinal))
        {
            _cachedArtistWidth = StringWidth.GetStringWidth(currentArtist, 400);
            _cachedArtistText = currentArtist;
            textChanged = true;
        }

        // maximum width limit, same as Windows native widget
        double maxLogicalWidth = _nativeWidgetsPadding / _scale;
        double logicalWidth;
        if (SettingsManager.Current.TaskbarWidgetFixedWidth)
        {
            // pin to maximum width so right-aligned controls don't shift between songs
            logicalWidth = maxLogicalWidth;
        }
        else
        {
            logicalWidth = Math.Max(_cachedTitleWidth, _cachedArtistWidth) + 55; // add margin for cover image
            logicalWidth = Math.Min(logicalWidth, maxLogicalWidth);
        }

        double newTitleContainerWidth = Math.Max(logicalWidth - 58, 0);
        double newArtistContainerWidth = Math.Max(logicalWidth - 58, 0);

        double availableTextWidth = Math.Max(maxLogicalWidth - 58, 0);

        bool scrollingTitleSetting = SettingsManager.Current.TaskbarWidgetScrollingTitleText;
        bool scrollingArtistSetting = SettingsManager.Current.TaskbarWidgetScrollingArtistText;
        int scrollingSpeed = SettingsManager.Current.TaskbarWidgetScrollingTextSpeed;
        bool scrollingLoop = SettingsManager.Current.TaskbarWidgetScrollingTextLoopForever;

        bool settingsChanged = _lastScrollingTitleSetting != scrollingTitleSetting ||
                               _lastScrollingArtistSetting != scrollingArtistSetting ||
                               _lastScrollingSpeed != scrollingSpeed ||
                               _lastScrollingLoop != scrollingLoop;

        bool titleSettingsChanged = _lastScrollingTitleSetting != scrollingTitleSetting ||
                                    _lastScrollingSpeed != scrollingSpeed ||
                                    _lastScrollingLoop != scrollingLoop;

        if (textChanged || _cachedTitleContainerWidth != newTitleContainerWidth || titleSettingsChanged)
        {
            SongTitleContainer.Width = newTitleContainerWidth;
            _cachedTitleContainerWidth = newTitleContainerWidth;

            UpdateMarquee(SongTitle, SongTitleContainer, _cachedTitleWidth, availableTextWidth, scrollingTitleSetting, scrollingSpeed, scrollingLoop);
        }

        bool artistSettingsChanged = _lastScrollingArtistSetting != scrollingArtistSetting ||
                                     _lastScrollingSpeed != scrollingSpeed ||
                                     _lastScrollingLoop != scrollingLoop;

        if (textChanged || _cachedArtistContainerWidth != newArtistContainerWidth || artistSettingsChanged)
        {
            SongArtistContainer.Width = newArtistContainerWidth;
            _cachedArtistContainerWidth = newArtistContainerWidth;

            UpdateMarquee(SongArtist, SongArtistContainer, _cachedArtistWidth, availableTextWidth, scrollingArtistSetting, scrollingSpeed, scrollingLoop);
        }

        if (settingsChanged)
        {
            _lastScrollingTitleSetting = scrollingTitleSetting;
            _lastScrollingArtistSetting = scrollingArtistSetting;
            _lastScrollingSpeed = scrollingSpeed;
            _lastScrollingLoop = scrollingLoop;
        }

        // add space for playback controls if enabled and visible
        if (SettingsManager.Current.TaskbarWidgetControlsEnabled && ControlsStackPanel.Visibility == Visibility.Visible)
        {
            logicalWidth += (int)(102);
        }

        double logicalHeight = 40; // default height

        return (logicalWidth, logicalHeight);
    }

    private void UpdateMarquee(System.Windows.Controls.TextBlock textBlock, Canvas container, double textWidth, double availableWidth, bool isEnabled, int speed, bool loopForever)
    {
        var transform = textBlock.RenderTransform as TranslateTransform;
        if (transform == null) return;

        bool isTitle = textBlock == SongTitle;
        double containerWidth = container.Width;

        if (isEnabled && textWidth > availableWidth && containerWidth > 0 && !double.IsNaN(containerWidth))
        {
            textBlock.Width = double.NaN;
            textBlock.TextTrimming = TextTrimming.None;

            string origText = isTitle ? _actualTitle : _actualArtist;

            ref double cachedMaskWidth = ref (isTitle ? ref _cachedTitleOpacityMaskWidth : ref _cachedArtistOpacityMaskWidth);
            ref LinearGradientBrush? cachedMask = ref (isTitle ? ref _cachedTitleOpacityMask : ref _cachedArtistOpacityMask);

            if (cachedMask == null || Math.Abs(containerWidth - cachedMaskWidth) > 0.5)
            {
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

            double scrollDistance = textWidth - containerWidth + 10;

            ref double lastScrollDistance = ref (isTitle ? ref _lastTitleScrollDistance : ref _lastArtistScrollDistance);
            ref int lastSpeed = ref (isTitle ? ref _lastTitleAnimSpeed : ref _lastArtistAnimSpeed);
            ref bool lastLoop = ref (isTitle ? ref _lastTitleLoopForever : ref _lastArtistLoopForever);

            if (loopForever)
            {
                // measure checks the actual rendered font, so we do this to ensure
                // the width works for any language

                textBlock.Text = origText + "     ";
                textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                scrollDistance = textBlock.DesiredSize.Width;

                textBlock.Text = origText + "     " + origText;
            }
            else
                textBlock.Text = origText;

            if (Math.Abs(scrollDistance - lastScrollDistance) > 0.5 || lastSpeed != speed || lastLoop != loopForever)
            {
                if (loopForever)
                {
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
                    double durationSeconds = scrollDistance / speed;
                    var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                    animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
                    animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + durationSeconds))));
                    animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + durationSeconds))));
                    animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + durationSeconds * 2))));
                    animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(6 + durationSeconds * 2))));
                    transform.BeginAnimation(TranslateTransform.XProperty, animation);
                }

                lastScrollDistance = scrollDistance;
                lastSpeed = speed;
                lastLoop = loopForever;
            }
        }
        else
        {
            if (isTitle) { _lastTitleScrollDistance = double.NaN; _lastTitleAnimSpeed = -1; }
            else { _lastArtistScrollDistance = double.NaN; _lastArtistAnimSpeed = -1; }

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
            // no media playing, hide UI
            Dispatcher.Invoke(() =>
            {
                _actualTitle = string.Empty;
                _actualArtist = string.Empty;

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
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
                TopBorder.BorderBrush = Brushes.Transparent;

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
            string newArtist = !string.IsNullOrEmpty(artist) ? artist : "-";

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

            // Update tooltip with song info
            SongInfoStackPanel.ToolTip = string.Empty;
            SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(title) ? title : string.Empty;
            SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(artist) ? "\n\n" + artist : string.Empty;

            if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
            {
                PlayPauseButton.Icon = _isPaused ? new SymbolIcon(SymbolRegular.Play24, filled: true) : new SymbolIcon(SymbolRegular.Pause24, filled: true);
            }

            // change color of icon
            SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0 ?
                BitmapHelper.SavedDominantColors.Last()
                : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
            SongImagePlaceholder.Foreground = brush;

            if (icon != null)
            {
                if (_isPaused && SettingsManager.Current.TaskbarWidgetShowPauseOverlay && !SettingsManager.Current.TaskbarWidgetControlsEnabled)
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
            SongArtist.Visibility = !string.IsNullOrEmpty(artist) ? Visibility.Visible : Visibility.Collapsed; // hide artist if it's not available
            SongInfoStackPanel.Visibility = Visibility.Visible;
            BackgroundImage.Visibility = SettingsManager.Current.TaskbarWidgetBackgroundBlur ? Visibility.Visible : Visibility.Collapsed;

            // on top of XAML visibility binding (XAML binding only hides when disabled in settings)
            ControlsStackPanel.Visibility = SettingsManager.Current.TaskbarWidgetControlsEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;

            Visibility = Visibility.Visible;
        });
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

        var focusedSession = _mainWindow.GetActiveMediaSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var focusedSession = _mainWindow.GetActiveMediaSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TryTogglePlayPauseAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var focusedSession = _mainWindow.GetActiveMediaSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TrySkipNextAsync();
    }
}