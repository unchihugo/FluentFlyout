// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using MicaWPF.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Windows.Media.Control;

namespace FluentFlyout.Controls
{
    public partial class TaskbarLyricsControl : UserControl
    {
        private static DispatcherTimer? _lyricsTimer;
        private static TaskbarLyricsControl? _instance;

        private MainWindow? _mainWindow;
        private string _currentTitle = string.Empty;
        private string _currentArtist = string.Empty;
        private List<LrcLine> _lyrics = new();
        private string _lastLineText = string.Empty;
        private bool _isFetching;
        private Storyboard? _scrollStoryboard;

        public TaskbarLyricsControl()
        {
            InitializeComponent();
            _instance = this;

            DataContext = SettingsManager.Current;

            if (MainBorder.Background is not SolidColorBrush)
            {
                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
            }

            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

            if (SettingsManager.Current.TaskbarLyricsEnabled)
            {
                StartLyricsTimer();
            }
        }

        public static void OnTaskbarLyricsEnabledChanged(bool value)
        {
            if (value)
            {
                StartLyricsTimer();
            }
            else
            {
                StopLyricsTimer();
                if (_instance != null)
                {
                    _instance.SetLyricsText(string.Empty);
                    _instance._lyrics.Clear();
                    _instance._currentTitle = string.Empty;
                    _instance._currentArtist = string.Empty;
                }
                SettingsManager.Current.TaskbarLyricsHasContent = false;
            }
        }

        private static void StartLyricsTimer()
        {
            if (_lyricsTimer == null)
            {
                _lyricsTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _lyricsTimer.Tick += LyricsTimer_Tick;
            }

            if (!_lyricsTimer.IsEnabled)
            {
                _lyricsTimer.Start();
            }
        }

        private static void StopLyricsTimer()
        {
            if (_lyricsTimer != null && _lyricsTimer.IsEnabled)
            {
                _lyricsTimer.Stop();
            }
        }

        private static async void LyricsTimer_Tick(object? sender, EventArgs e)
        {
            if (_instance == null) return;
            if (_instance._isFetching) return;

            try
            {
                if (_instance._mainWindow == null)
                {
                    _instance._mainWindow = (MainWindow)Application.Current.MainWindow;
                    if (_instance._mainWindow == null) return;
                }

                var session = _instance._mainWindow.GetActiveMediaSession();
                if (session == null)
                {
                    _instance.SetLyricsText(string.Empty);
                    _instance._currentTitle = string.Empty;
                    _instance._currentArtist = string.Empty;
                    _instance._lyrics.Clear();
                    SettingsManager.Current.TaskbarLyricsHasContent = false;
                    return;
                }

                var playbackInfo = session.ControlSession.GetPlaybackInfo();
                if (playbackInfo == null || playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)
                {
                    _instance.SetLyricsText(string.Empty);
                    _instance._lyrics.Clear();
                    SettingsManager.Current.TaskbarLyricsHasContent = false;
                    return;
                }

                var mediaProps = await TryGetMediaPropertiesAsync(session.ControlSession);
                if (mediaProps == null || string.IsNullOrEmpty(mediaProps.Title))
                {
                    _instance.SetLyricsText(string.Empty);
                    _instance._lyrics.Clear();
                    SettingsManager.Current.TaskbarLyricsHasContent = false;
                    return;
                }

                if (mediaProps.Title != _instance._currentTitle || mediaProps.Artist != _instance._currentArtist)
                {
                    _instance._currentTitle = mediaProps.Title;
                    _instance._currentArtist = mediaProps.Artist;
                    _instance.SetLyricsText("...");
                    _instance._lyrics.Clear();
                    _instance._isFetching = true;
                    SettingsManager.Current.TaskbarLyricsHasContent = true;

                    string targetTitle = _instance._currentTitle;
                    string targetArtist = _instance._currentArtist;
                    var timeline = session.ControlSession.GetTimelineProperties();
                    double duration = timeline.EndTime.TotalSeconds;

                    _lyricsTimer?.Stop();
                    try
                    {
                        var fetchedLyrics = await LyricsManager.FetchLyricsAsync(targetArtist, targetTitle, duration);
                        try
                        {
                            NLog.LogManager.GetCurrentClassLogger().Info($"Fetched {fetchedLyrics.Count} lyric lines for '{targetArtist} - {targetTitle}'");
                        }
                        catch { }

                        if (_instance != null && _instance._currentTitle == targetTitle && _instance._currentArtist == targetArtist)
                        {
                            _instance._lyrics = fetchedLyrics;
                            if (_instance._lyrics.Count == 0)
                            {
                                _instance.SetLyricsText(string.Empty);
                            }
                        }
                    }
                    finally
                    {
                        if (_instance != null) _instance._isFetching = false;
                        _lyricsTimer?.Start();
                    }
                    return;
                }

                if (_instance._lyrics.Count > 0)
                {
                    var timeline = session.ControlSession.GetTimelineProperties();
                    var pos = timeline.Position;
                    TimeSpan timeSinceLastUpdate = TimeSpan.Zero;
                    if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        timeSinceLastUpdate = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                        if (timeSinceLastUpdate.TotalSeconds > 0)
                        {
                            pos += timeSinceLastUpdate;
                        }
                    }

                    if (pos > timeline.EndTime)
                    {
                        pos = timeline.EndTime;
                    }

                    var lookupPos = pos + TimeSpan.FromMilliseconds(SettingsManager.Current.TaskbarLyricsDelay);
                    if (lookupPos < TimeSpan.Zero)
                    {
                        lookupPos = TimeSpan.Zero;
                    }
                    else if (lookupPos > timeline.EndTime)
                    {
                        lookupPos = timeline.EndTime;
                    }

                    var currentLine = _instance._lyrics.LastOrDefault(x => x.Timestamp <= lookupPos);
                    string newText = currentLine != null ? currentLine.Text : string.Empty;

                    try
                    {
                        NLog.LogManager.GetCurrentClassLogger().Info($"Lyrics Sync: pos={pos.TotalSeconds:F2}s, lookupPos={lookupPos.TotalSeconds:F2}s, delay={SettingsManager.Current.TaskbarLyricsDelay}ms, timeline.Position={timeline.Position.TotalSeconds:F2}s, lastUpdated={timeline.LastUpdatedTime:yyyy-MM-dd HH:mm:ss.fff zzz}, utcNow={DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff zzz}, diff={timeSinceLastUpdate.TotalSeconds:F2}s, lyricsCount={_instance._lyrics.Count}, text='{newText}'");
                    }
                    catch { }

                    if (newText != _instance._lastLineText)
                    {
                        _instance._lastLineText = newText;
                        _instance.SetLyricsText(newText);
                    }
                }
                else
                {
                    try
                    {
                        NLog.LogManager.GetCurrentClassLogger().Info($"Lyrics Sync: No lyrics loaded (_lyrics.Count = 0). Current title: '{_instance._currentTitle}', artist: '{_instance._currentArtist}'");
                    }
                    catch { }
                }

                bool hasContent = _instance._lyrics.Count > 0;
                if (SettingsManager.Current.TaskbarLyricsHasContent != hasContent)
                {
                    SettingsManager.Current.TaskbarLyricsHasContent = hasContent;
                }
            }
            catch (Exception ex)
            {
                try { NLog.LogManager.GetCurrentClassLogger().Warn(ex, "LyricsTimer_Tick error (suppressed)"); } catch { }
                if (_instance != null) _instance._isFetching = false;
                if (SettingsManager.Current.TaskbarLyricsHasContent)
                    SettingsManager.Current.TaskbarLyricsHasContent = false;
                _lyricsTimer?.Start();
            }
        }

        private static async Task<GlobalSystemMediaTransportControlsSessionMediaProperties?> TryGetMediaPropertiesAsync(GlobalSystemMediaTransportControlsSession controlSession)
        {
            try
            {
                return await controlSession.TryGetMediaPropertiesAsync();
            }
            catch
            {
                return null;
            }
        }

        private void Grid_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!SettingsManager.Current.TaskbarLyricsClickable || !SettingsManager.Current.TaskbarLyricsHasContent) return;

            SolidColorBrush targetBackgroundBrush;
            WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
            bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;
            if (isDark)
            {
                targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
                TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };
            }
            else
            {
                targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) { Opacity = 0.6 };
                TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 1 };
            }

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
            if (!SettingsManager.Current.TaskbarLyricsClickable || !SettingsManager.Current.TaskbarLyricsHasContent) return;

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
            if (!SettingsManager.Current.TaskbarLyricsClickable || !SettingsManager.Current.TaskbarLyricsHasContent) return;

            SettingsWindow.ShowInstance("TaskbarLyricsPage");
        }

        private void ClippedGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTextLayout();
        }

        private void LyricsText_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTextLayout();
        }

        private void UpdateTextLayout()
        {
            if (ClippedGrid == null || LyricsText == null) return;

            _scrollStoryboard?.Stop();
            _scrollStoryboard = null;
            LyricsText.BeginAnimation(Canvas.LeftProperty, null);
            Canvas.SetLeft(LyricsText, 0);

            double containerWidth = ClippedGrid.ActualWidth;
            double textWidth = LyricsText.ActualWidth;

            if (containerWidth <= 0 || textWidth <= 0)
            {
                return;
            }

            if (textWidth <= containerWidth)
            {
                double left = (containerWidth - textWidth) / 2.0;
                Canvas.SetLeft(LyricsText, left);
            }
            else
            {
                Canvas.SetLeft(LyricsText, 0);

                double scrollDistance = textWidth - containerWidth + 12;
                double speed = 35.0;
                double durationSec = scrollDistance / speed;

                var keyFrameAnimation = new DoubleAnimationUsingKeyFrames();
                keyFrameAnimation.RepeatBehavior = RepeatBehavior.Forever;

                keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));

                TimeSpan scrollTime = TimeSpan.FromSeconds(1.5 + durationSec);
                keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance, KeyTime.FromTimeSpan(scrollTime)));

                TimeSpan pauseTime = scrollTime + TimeSpan.FromSeconds(1.5);
                keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance, KeyTime.FromTimeSpan(pauseTime)));

                TimeSpan scrollBackTime = pauseTime + TimeSpan.FromSeconds(durationSec);
                keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(scrollBackTime)));

                TimeSpan finalTime = scrollBackTime + TimeSpan.FromSeconds(1.5);
                keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(finalTime)));

                Storyboard.SetTarget(keyFrameAnimation, LyricsText);
                Storyboard.SetTargetProperty(keyFrameAnimation, new PropertyPath(Canvas.LeftProperty));

                _scrollStoryboard = new Storyboard();
                _scrollStoryboard.Children.Add(keyFrameAnimation);
                _scrollStoryboard.Begin();
            }
        }

        private void SetLyricsText(string text)
        {
            if (LyricsText == null) return;
            if (LyricsText.Text == text) return;

            LyricsText.Text = text;
            var fadeIn = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(200));
            LyricsText.BeginAnimation(OpacityProperty, fadeIn);
        }
    }
}