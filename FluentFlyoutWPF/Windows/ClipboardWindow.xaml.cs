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
/// Interaction logic for ClipboardWindow.xaml
/// </summary>
public partial class ClipboardWindow : MicaWindow
{
    private CancellationTokenSource cts = new();
    private MainWindow _mainWindow = (MainWindow)Application.Current.MainWindow;
    private bool _isHiding = true;
    private MonitorInfo _openedMonitor;

    public ClipboardWindow()
    {
        DataContext = SettingsManager.Current;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);
        CustomWindowChrome.CaptionHeight = 0;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Top = -9999; // start off-screen
        Left = SystemParameters.WorkArea.Width / 2 - Width / 2;
    }

    private void animateSymbol()
    {
        Dispatcher.Invoke(() =>
        {
            int msDuration = (int)(MainWindow.getDuration() / 1.5);

            if (SettingsManager.Current.LockKeysAnimated) // Reuse same animation setting as lock keys for simplicity
            {
                // animate checkmark scale
                var scaleAnim = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(msDuration)
                };
                scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.0)));
                scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromPercent(0.7), new CubicEase { EasingMode = EasingMode.EaseOut }));
                scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), new CubicEase { EasingMode = EasingMode.EaseInOut }));

                CheckScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnim);
                CheckScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnim);
            }
            else
            {
                CheckScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
                CheckScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
                CheckScale.ScaleX = 1.0;
                CheckScale.ScaleY = 1.0;
            }
        });
    }

    public async void ShowClipboardFlyout()
    {
        if (SettingsManager.Current.ClipboardFlyoutAcrylicWindowEnabled)
        {
            WindowBlurHelper.EnableBlur(this);
        }
        else
        {
            WindowBlurHelper.DisableBlur(this);
        }

        // Try to get translated string, otherwise use default
        try
        {
            string? translated = FindResource("ClipboardWindow_Copied")?.ToString();
            if (!string.IsNullOrEmpty(translated))
            {
                ClipboardTextBlock.Text = translated;
            }
        }
        catch
        {
            // Default already set in XAML
        }

        if (LocalizationManager.LanguageCode != "en")
        {
            Width = LocalizationManager.maxLength + 56.0;
        }
        else
        {
            Width = 200; // default width for this longer text
        }

        animateSymbol();

        if (_isHiding)
        {
            _isHiding = false;
            _openedMonitor = GetPreferredTargetDisplay();
            _mainWindow.OpenAnimation(window: this, alwaysBottom: true, selectedMonitor: _openedMonitor);
        }
        cts.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(SettingsManager.Current.ClipboardFlyoutDuration, token);
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
