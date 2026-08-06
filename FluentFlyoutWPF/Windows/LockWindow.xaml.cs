// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using MicaWPF.Controls;
using System.Windows;
using System.Windows.Media.Animation;
using static FluentFlyoutWPF.Classes.Utils.MonitorUtil;


namespace FluentFlyoutWPF.Windows;

/// <summary>
/// Interaction logic for LockWindow.xaml
/// </summary>
public partial class LockWindow : MicaWindow
{
    private CancellationTokenSource cts;
    private CancellationTokenSource? _transitionCts;
    private MainWindow _mainWindow = (MainWindow)Application.Current.MainWindow;
    private bool _isHiding = true;
    private MonitorInfo _openedMonitor;

    public LockWindow()
    {
        DataContext = SettingsManager.Current;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);
        CustomWindowChrome.CaptionHeight = 0;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Top = -9999; // start off-screen
        Left = SystemParameters.WorkArea.Width / 2 - Width / 2;
        cts = new CancellationTokenSource();
    }

    private void setStatus(string key, bool isOn)
    {
        Dispatcher.Invoke(() =>
        {
            if (key == "Insert")
            {
                // not sure how to properly check if overwrite or insert as every program has different behavior
                //if (isOn) LockTextBlock.Text = "Insert mode";
                //else LockTextBlock.Text = "Overwrite mode";
                LockTextBlock.Text = FindResource("LockWindow_InsertPressed").ToString();
                isOn = true;
            }
            else LockTextBlock.Text = key + " " + (isOn ? FindResource("LockWindow_LockOn").ToString() : FindResource("LockWindow_LockOff").ToString());

            LockTextBlock.FontWeight = SettingsManager.Current.LockKeysBoldUi ? FontWeights.Medium : FontWeights.Normal;

            double targetOpacity = isOn ? 1.0 : 0.2;
            double targetWidth = isOn ? 60.0 : 36.0;

            double targetShackleAngle = isOn ? 0.0 : 25.0;
            double targetShackleBounceY = 0.0;

            int msDuration = (int)(MainWindow.getDuration() / 1.5);

            if (SettingsManager.Current.LockKeysAnimated)
            {
                // animate indicator opacity
                var opacityAnim = new DoubleAnimation
                {
                    To = targetOpacity,
                    Duration = TimeSpan.FromMilliseconds(msDuration),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                LockIndicatorRectangle.BeginAnimation(OpacityProperty, opacityAnim);

                // animate indicator width
                var widthAnim = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(msDuration),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                LockIndicatorRectangle.BeginAnimation(WidthProperty, widthAnim);

                // animate shackle rotation (open/close)
                var rotationAnim = new DoubleAnimation
                {
                    To = targetShackleAngle,
                    Duration = TimeSpan.FromMilliseconds(msDuration),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ShackleRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotationAnim);

                // animate shackle bounce
                var bounceAnim = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(msDuration)
                };
                bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.1),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(targetShackleBounceY, KeyTime.FromPercent(1.0),
                    new CubicEase { EasingMode = EasingMode.EaseInOut }));
                ShackleBounce.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, bounceAnim);

            }
            else
            {
                LockIndicatorRectangle.BeginAnimation(OpacityProperty, null);
                LockIndicatorRectangle.BeginAnimation(WidthProperty, null);
                ShackleRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
                ShackleBounce.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

                LockIndicatorRectangle.Opacity = targetOpacity;
                LockIndicatorRectangle.Width = targetWidth;
                ShackleRotation.Angle = targetShackleAngle;
                ShackleBounce.Y = targetShackleBounceY;
            }
        });
    }

    public async void ShowLockFlyout(string? key, bool isOn)
    {
        if (string.IsNullOrEmpty(key)) return;

        await Dispatcher.InvokeAsync(async () =>
        {
            _transitionCts?.Cancel();
            _transitionCts?.Dispose();
            _transitionCts = new CancellationTokenSource();
            var transitionToken = _transitionCts.Token;

            if (SettingsManager.Current.LockKeysAcrylicWindowEnabled)
            {
                WindowBlurHelper.EnableBlur(this);
            }
            else
            {
                WindowBlurHelper.DisableBlur(this);
            }

            bool isModeSwitch = LanguageContent.Visibility == Visibility.Visible;

            if (isModeSwitch)
            {
                LanguageContent.BeginAnimation(UIElement.OpacityProperty, null);
                var fadeOutAnim = new DoubleAnimation
                {
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                LanguageContent.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
                try
                {
                    await Task.Delay(100, transitionToken);
                }
                catch (OperationCanceledException) { return; }
            }

            setStatus(key, isOn);

            LockKeysContent.Visibility = Visibility.Visible;
            LanguageContent.Visibility = Visibility.Collapsed;

            LockKeysContent.BeginAnimation(UIElement.OpacityProperty, null);
            LanguageContent.BeginAnimation(UIElement.OpacityProperty, null);

            LockKeysContent.Opacity = 1.0;

            // Calculate target dimensions & position
            double targetWidth = 160;
            if (LocalizationManager.LanguageCode != "en")
            {
                targetWidth = LocalizationManager.maxLength + 56.0;
            }

            _openedMonitor = GetPreferredTargetDisplay();
            double newRawWidth = Math.Ceiling(targetWidth * _openedMonitor.dpiX / 96.0);
            double newLeft = Math.Ceiling(_openedMonitor.workArea.Left + (_openedMonitor.workArea.Width / 2) - (newRawWidth / 2)) * 96.0 / _openedMonitor.dpiX;

            this.BeginAnimation(Window.WidthProperty, null);
            this.BeginAnimation(Window.LeftProperty, null);

            Width = targetWidth;
            Left = newLeft;
            this.UpdateLayout();

            if (_isHiding)
            {
                _isHiding = false;
                _mainWindow.OpenAnimation(window: this, alwaysBottom: true, selectedMonitor: _openedMonitor);
            }
        });

        cts.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(SettingsManager.Current.LockKeysDuration, token);
                _mainWindow.CloseAnimation(window: this, selectedMonitor: _openedMonitor);
                _isHiding = true;
                await Task.Delay(MainWindow.getDuration());
                if (_isHiding == false) return;

                WindowHelper.SetVisibility(this, false);
                break;
            }
        }
        catch (TaskCanceledException)
        {
            // do nothing
        }
    }

    private static MonitorInfo GetPreferredTargetDisplay()
    {
        return SettingsManager.Current.LockKeysMonitorPreference switch
        {
            1 => GetMonitorWithFocusedWindow(),
            2 => GetMonitorWithCursor(),
            _ => GetSelectedMonitor(SettingsManager.Current.FlyoutSelectedMonitor),
        };
    }

}