// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static FluentFlyoutWPF.Classes.Utils.MonitorUtil;

namespace FluentFlyoutWPF.Windows;

public partial class LockWindow
{
    public async void ShowLanguageFlyout()
    {
        if (!SettingsManager.Current.LanguageFlyoutEnabled) return;

        await Dispatcher.InvokeAsync(async () =>
        {
            // Cancel any active layout transition immediately to support fast clicks
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

            // Get current keyboard layout
            IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero) foregroundWindow = NativeMethods.FindWindow("Shell_TrayWnd", null);
            uint threadId = NativeMethods.GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
            IntPtr hkl = NativeMethods.GetKeyboardLayout(threadId);
            if (hkl == IntPtr.Zero) hkl = NativeMethods.GetKeyboardLayout(0);

            int lcid = (int)((long)hkl & 0xFFFF);

            try
            {
                CultureInfo culture = new CultureInfo(lcid);
                string langCode = culture.TwoLetterISOLanguageName;
                string name = culture.NativeName;
                if (!SettingsManager.Current.LanguageFlyoutShowRegion)
                {
                    int parenIndex = name.IndexOf('(');
                    if (parenIndex > 0) name = name.Substring(0, parenIndex).Trim();
                }
                if (!string.IsNullOrEmpty(name)) name = char.ToUpper(name[0]) + name.Substring(1);

                double targetWidth = SettingsManager.Current.LanguageFlyoutWidth;
                if (!SettingsManager.Current.LanguageFlyoutShowRegion)
                {
                    targetWidth *= 0.6;
                }
                var monitor = GetSelectedMonitor(SettingsManager.Current.FlyoutSelectedMonitor);
                double newRawWidth = Math.Ceiling(targetWidth * monitor.dpiX / 96.0);
                double newLeft = Math.Ceiling(monitor.workArea.Left + (monitor.workArea.Width / 2) - (newRawWidth / 2)) * 96.0 / monitor.dpiX;

                if (_isHiding)
                {
                    _isHiding = false;

                    // Set contents instantly
                    LangShortText.Text = langCode.ToUpper();
                    LangFullText.Text = name;

                    // Reset LockIndicator opacity and width
                    LockIndicatorRectangle.BeginAnimation(OpacityProperty, null);
                    LockIndicatorRectangle.BeginAnimation(WidthProperty, null);
                    LockIndicatorRectangle.Opacity = 1.0;
                    LockIndicatorRectangle.Width = 60.0;

                    LockKeysContent.Visibility = Visibility.Collapsed;
                    LanguageContent.Visibility = Visibility.Visible;

                    LanguageContent.BeginAnimation(UIElement.OpacityProperty, null);
                    LanguageContent.Opacity = 1.0;

                    this.BeginAnimation(Window.WidthProperty, null);
                    this.BeginAnimation(Window.LeftProperty, null);

                    Width = targetWidth;
                    Left = newLeft;
                    this.UpdateLayout();

                    _openedMonitor = monitor;
                    _mainWindow.OpenAnimation(window: this, alwaysBottom: true, selectedMonitor: _openedMonitor);
                }
                else
                {
                    bool isModeSwitch = LockKeysContent.Visibility == Visibility.Visible;

                    // Fade out current content if switching modes
                    if (isModeSwitch)
                    {
                        LockKeysContent.BeginAnimation(UIElement.OpacityProperty, null);
                        var fadeOutAnim = new DoubleAnimation
                        {
                            To = 0.0,
                            Duration = TimeSpan.FromMilliseconds(100),
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                        };
                        LockKeysContent.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
                        await Task.Delay(100, transitionToken);
                    }

                    // Update contents
                    LangShortText.Text = langCode.ToUpper();
                    LangFullText.Text = name;

                    LockKeysContent.Visibility = Visibility.Collapsed;
                    LanguageContent.Visibility = Visibility.Visible;

                    LockIndicatorRectangle.BeginAnimation(OpacityProperty, null);
                    LockIndicatorRectangle.BeginAnimation(WidthProperty, null);
                    LockIndicatorRectangle.Opacity = 1.0;
                    LockIndicatorRectangle.Width = 60.0;

                    Width = targetWidth;
                    Left = newLeft;
                    this.UpdateLayout();

                    LanguageContent.BeginAnimation(UIElement.OpacityProperty, null);
                    var fadeInAnim = new DoubleAnimation
                    {
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(150),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    LanguageContent.BeginAnimation(UIElement.OpacityProperty, fadeInAnim);
                }
            }
            catch (OperationCanceledException)
            {
                // Transition was cancelled by a newer keypress
            }
            catch
            {
                LangShortText.Text = "??";
                LangFullText.Text = "Language";
                if (_isHiding)
                {
                    _isHiding = false;
                    double targetWidth = SettingsManager.Current.LanguageFlyoutWidth;
                    if (!SettingsManager.Current.LanguageFlyoutShowRegion)
                    {
                        targetWidth *= 0.6;
                    }
                    Width = targetWidth;
                    _openedMonitor = GetSelectedMonitor(SettingsManager.Current.FlyoutSelectedMonitor);
                    _mainWindow.OpenAnimation(window: this, alwaysBottom: true, selectedMonitor: _openedMonitor);
                }
            }
        });

        cts.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(SettingsManager.Current.LanguageFlyoutDuration, token);
                _mainWindow.CloseAnimation(window: this, selectedMonitor: _openedMonitor);
                _isHiding = true;
                await Task.Delay(MainWindow.getDuration());
                if (_isHiding == false) return;

                WindowHelper.SetVisibility(this, false);
                break;
            }
        }
        catch (TaskCanceledException) { }
    }
}