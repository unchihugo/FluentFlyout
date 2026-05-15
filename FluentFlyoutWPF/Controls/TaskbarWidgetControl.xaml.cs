// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes.Utils;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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

    private readonly double _scale = 0.9;
    private readonly int _nativeWidgetsPadding = 216;
    private const double MediaOnlyLogicalWidth = 55;
    private const double PlaybackControlsLogicalWidth = 102;

    // Cached width calculations
    private string _cachedTitleText = string.Empty;
    private string _cachedArtistText = string.Empty;
    private double _cachedTitleWidth = 0;
    private double _cachedArtistWidth = 0;
    private readonly SystemUsageReader _systemUsageReader = new();
    private readonly DispatcherTimer _systemStatsTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };

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

        _systemStatsTimer.Tick += (s, e) => UpdateSystemStats();
        Unloaded += (s, e) =>
        {
            _systemStatsTimer.Stop();
            _systemUsageReader.Dispose();
        };
        UpdateSystemStatsVisibility();

        // Initialize control order
        ReorderControls();
    }

    public void ReorderControls()
    {
        // Remove configurable sections from MainStackPanel
        MainStackPanel.Children.Remove(SystemStatsStackPanel);
        MainStackPanel.Children.Remove(ControlsStackPanel);

        if (SettingsManager.Current.TaskbarWidgetSystemStatsPosition == 0)
        {
            MainStackPanel.Children.Insert(0, SystemStatsStackPanel);
            SystemStatsStackPanel.Margin = new Thickness(0, 0, 8, 0);
        }
        else
        {
            MainStackPanel.Children.Add(SystemStatsStackPanel);
            SystemStatsStackPanel.Margin = new Thickness(10, 0, 0, 0);
        }

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
        var foreground = GetWindowsThemeForegroundBrush();

        SongTitle.Foreground = foreground;
        SongArtist.Foreground = foreground;
        PreviousButton.Foreground = foreground;
        PlayPauseButton.Foreground = foreground;
        NextButton.Foreground = foreground;
        ApplySystemStatsStyle();
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
        string currentTitle = SongTitle.Text;
        string currentArtist = SongArtist.Text;

        if (!string.Equals(currentTitle, _cachedTitleText, StringComparison.Ordinal))
        {
            _cachedTitleWidth = StringWidth.GetStringWidth(currentTitle, 400);
            _cachedTitleText = currentTitle;
        }
        if (!string.Equals(currentArtist, _cachedArtistText, StringComparison.Ordinal))
        {
            _cachedArtistWidth = StringWidth.GetStringWidth(currentArtist, 400);
            _cachedArtistText = currentArtist;
        }

        UpdateSystemStatsVisibility();

        bool mediaVisible = SongImageBorder.Visibility == Visibility.Visible
            || !string.IsNullOrEmpty(currentTitle + currentArtist);

        double mediaWidth = mediaVisible
            ? Math.Max(_cachedTitleWidth, _cachedArtistWidth) + MediaOnlyLogicalWidth // add margin for cover image
            : 0;

        // maximum width limit, same as Windows native widget
        mediaWidth = Math.Min(mediaWidth, _nativeWidgetsPadding / _scale);

        SongTitle.Width = Math.Max(mediaWidth - 58, 0);
        SongArtist.Width = Math.Max(mediaWidth - 58, 0);

        double logicalWidth = mediaWidth;

        if (SystemStatsStackPanel.Visibility == Visibility.Visible)
        {
            logicalWidth += GetSystemStatsLogicalWidth();
        }

        if (logicalWidth <= 0)
        {
            logicalWidth = MediaOnlyLogicalWidth;
        }

        // add space for playback controls if enabled and visible
        if (SettingsManager.Current.TaskbarWidgetControlsEnabled && ControlsStackPanel.Visibility == Visibility.Visible)
        {
            logicalWidth += PlaybackControlsLogicalWidth;
        }


        double logicalHeight = 40; // default height

        return (logicalWidth, logicalHeight);
    }

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
    {
        if (title == "-" && artist == "-")
        {
            // no media playing, hide UI
            Dispatcher.Invoke(() =>
            {
                UpdateSystemStatsVisibility();
                bool showSystemStats = IsSystemStatsEnabled();

                if (SettingsManager.Current.TaskbarWidgetHideCompletely && !showSystemStats)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                ControlsStackPanel.Visibility = Visibility.Collapsed;
                SongTitle.Text = string.Empty;
                SongArtist.Text = string.Empty;
                SongInfoStackPanel.Visibility = Visibility.Collapsed;
                SongInfoStackPanel.ToolTip = string.Empty;
                SongImageBorder.Visibility = showSystemStats ? Visibility.Collapsed : Visibility.Visible;
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
            if (SongTitle.Text != title && SongArtist.Text != artist)
            {
                // changed info
                if (SettingsManager.Current.TaskbarWidgetAnimated)
                {
                    AnimateEntrance();
                }
            }

            SongTitle.Text = !string.IsNullOrEmpty(title) ? title : "-";
            SongArtist.Text = !string.IsNullOrEmpty(artist) ? artist : "-";
            SongImageBorder.Visibility = Visibility.Visible;

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
            UpdateSystemStatsVisibility();

            Visibility = Visibility.Visible;
        });
    }

    private bool IsSystemStatsEnabled()
    {
        return SettingsManager.Current.TaskbarWidgetSystemStatsEnabled
            && SettingsManager.Current.TaskbarWidgetEnabled;
    }

    private void UpdateSystemStatsVisibility()
    {
        ApplySystemStatsStyle();

        bool enabled = IsSystemStatsEnabled();
        SystemStatsStackPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (!enabled)
        {
            if (_systemStatsTimer.IsEnabled)
            {
                _systemStatsTimer.Stop();
            }

            SystemCpuStatsText.Text = SettingsManager.Current.TaskbarWidgetCpuTemperatureEnabled
                ? "CPU --% · --°C"
                : "CPU --%";
            SystemRamStatsText.Text = "RAM --%";
            return;
        }

        if (!_systemStatsTimer.IsEnabled)
        {
            UpdateSystemStats();
            _systemStatsTimer.Start();
        }
    }

    private void UpdateSystemStats()
    {
        try
        {
            bool showCpuTemperature = SettingsManager.Current.TaskbarWidgetCpuTemperatureEnabled;
            SystemUsageSnapshot snapshot = _systemUsageReader.Read(showCpuTemperature);
            SystemUsageDisplayText text = SystemUsageTextFormatter.FormatLines(snapshot, showCpuTemperature);
            SystemCpuStatsText.Text = text.CpuText;
            SystemRamStatsText.Text = text.RamText;
            ApplySystemStatsForeground(snapshot.CpuTemperatureCelsius, showCpuTemperature);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Taskbar Widget failed to update system usage stats");
        }
    }

    private void ApplySystemStatsStyle()
    {
        string fontFamily = SystemUsageStyleHelper.NormalizeFontFamily(
            SettingsManager.Current.TaskbarWidgetSystemStatsFontFamily,
            SystemUsageStyleHelper.DefaultFontFamily);

        FontFamily statsFontFamily = new(fontFamily);
        SongTitle.FontFamily = statsFontFamily;
        SongArtist.FontFamily = statsFontFamily;
        SystemCpuStatsText.FontFamily = statsFontFamily;
        SystemRamStatsText.FontFamily = statsFontFamily;

        double fontSize = SystemUsageStyleHelper.NormalizeFontSize(SettingsManager.Current.TaskbarWidgetSystemStatsFontSize);
        SystemCpuStatsText.FontSize = fontSize;
        SystemRamStatsText.FontSize = fontSize;
        SystemStatsStackPanel.Width = GetSystemStatsPanelWidth(fontSize, SettingsManager.Current.TaskbarWidgetCpuTemperatureEnabled);

        ApplySystemStatsForeground(null, showCpuTemperature: false);
    }

    private void ApplySystemStatsForeground(int? cpuTemperatureCelsius, bool showCpuTemperature)
    {
        Brush foreground = SystemUsageStyleHelper.TryParseColor(SettingsManager.Current.TaskbarWidgetSystemStatsColor, out Color color)
            ? new SolidColorBrush(color)
            : GetWindowsThemeForegroundBrush();

        SystemRamStatsText.Foreground = foreground;

        SystemCpuStatsText.Foreground = showCpuTemperature
            && SystemUsageStyleHelper.TryGetCpuTemperatureColor(cpuTemperatureCelsius, out Color cpuTemperatureColor)
                ? new SolidColorBrush(cpuTemperatureColor)
                : foreground;
    }

    private static SolidColorBrush GetWindowsThemeForegroundBrush()
    {
        WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
        bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;

        return new SolidColorBrush(isDark
            ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0xE4, 0x1C, 0x1C, 0x1C));
    }

    private double GetSystemStatsLogicalWidth()
    {
        return GetSystemStatsPanelWidth(
            SystemUsageStyleHelper.NormalizeFontSize(SettingsManager.Current.TaskbarWidgetSystemStatsFontSize),
            SettingsManager.Current.TaskbarWidgetCpuTemperatureEnabled) + 10;
    }

    private static double GetSystemStatsPanelWidth(double fontSize, bool showCpuTemperature)
    {
        return showCpuTemperature
            ? Math.Clamp(fontSize * 10.4, 116, 156)
            : Math.Clamp(fontSize * 6.2, 72, 96);
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

        var mediaManager = _mainWindow.mediaManager;
        if (mediaManager == null) return;

        var focusedSession = mediaManager.GetFocusedSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var mediaManager = _mainWindow.mediaManager;
        if (mediaManager == null) return;

        var focusedSession = mediaManager.GetFocusedSession();
        if (focusedSession == null) return;

        if (_isPaused) // paused
        {
            await focusedSession.ControlSession.TryPlayAsync();
        }
        else // playing
        {
            await focusedSession.ControlSession.TryPauseAsync();
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var mediaManager = _mainWindow.mediaManager;
        if (mediaManager == null) return;

        var focusedSession = mediaManager.GetFocusedSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TrySkipNextAsync();
    }
}