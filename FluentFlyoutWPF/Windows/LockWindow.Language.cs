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
    private Color GetShiftedColor(Color baseColor, string languageCode, string seedText)
    {
        int mode = SettingsManager.Current.LanguageFlyoutColorMode;

        // Mode 1: Always System Accent
        if (mode == 1) return baseColor;

        // Mode 0: Auto (System for primary, Unique for secondary)
        if (mode == 0)
        {
            string systemLangCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (languageCode.Equals(systemLangCode, StringComparison.OrdinalIgnoreCase)) return baseColor;
        }

        // Mode 2 (Unique) or secondary in Mode 0
        int seed = 0;
        foreach (char c in seedText) seed += (int)c;

        System.Drawing.Color drawingColor = System.Drawing.Color.FromArgb(baseColor.A, baseColor.R, baseColor.G, baseColor.B);
        float hue = drawingColor.GetHue();
        float saturation = drawingColor.GetSaturation();
        float brightness = drawingColor.GetBrightness();

        // Shift hue significantly (between 60 and 300 degrees) to be visually different
        float hueShift = 60 + (Math.Abs(seed) % 240);
        float newHue = (hue + hueShift) % 360;

        return ColorFromAhsb(baseColor.A, newHue, saturation, brightness);
    }

    private Color ColorFromAhsb(byte a, float h, float s, float l)
    {
        float r, g, b;
        if (s == 0) r = g = b = l;
        else
        {
            float q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            float p = 2 * l - q;
            r = HueToRgb(p, q, h / 360f + 1f / 3f);
            g = HueToRgb(p, q, h / 360f);
            b = HueToRgb(p, q, h / 360f - 1f / 3f);
        }
        return Color.FromArgb(a, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private float HueToRgb(float p, float q, float t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1f / 6f) return p + (q - p) * 6 * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6;
        return p;
    }

    public async void ShowLanguageFlyout()
    {
        if (!SettingsManager.Current.LanguageFlyoutEnabled) return;

        await Dispatcher.InvokeAsync(async () =>
        {
            // Cancel any active layout transition immediately to support fast clicks (spam)
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

                object colorObj = Application.Current.TryFindResource("MicaWPF.Colors.AccentFillColorDefault");
                Color systemColor = colorObj is Color c ? c : ((SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.AccentFillColorDefault")).Color;

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
                    LockIndicatorRectangle.Fill = new SolidColorBrush(GetShiftedColor(systemColor, langCode, name));

                    // Reset LockIndicator opacity and width in case it was animated/left by lock keys
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
                    this.UpdateLayout(); // Force immediate layout centering!

                    _openedMonitor = monitor;
                    _mainWindow.OpenAnimation(window: this, alwaysBottom: true, selectedMonitor: _openedMonitor);
                }
                else
                {
                    bool isModeSwitch = LockKeysContent.Visibility == Visibility.Visible;

                    // Fade out current content
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
                    }
                    else
                    {
                        LanguageContent.BeginAnimation(UIElement.OpacityProperty, null);
                        var fadeOutAnim = new DoubleAnimation
                        {
                            To = 0.0,
                            Duration = TimeSpan.FromMilliseconds(100),
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                        };
                        LanguageContent.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
                    }

                    // 2. Wait for the fade out to complete (supports cancellation under rapid clicks)
                    await Task.Delay(100, transitionToken);

                    // 3. Update contents while invisible
                    LangShortText.Text = langCode.ToUpper();
                    LangFullText.Text = name;

                    LockKeysContent.Visibility = Visibility.Collapsed;
                    LanguageContent.Visibility = Visibility.Visible;

                    // Reset LockIndicator opacity and width in case it was animated/left by lock keys
                    LockIndicatorRectangle.BeginAnimation(OpacityProperty, null);
                    LockIndicatorRectangle.BeginAnimation(WidthProperty, null);
                    LockIndicatorRectangle.Opacity = 1.0;
                    LockIndicatorRectangle.Width = 60.0;

                    // Ensure dimensions are correct (instant positioning, no animations or rendering frame-by-frame loop)
                    Width = targetWidth;
                    Left = newLeft;
                    this.UpdateLayout();

                    // 4. Smoothly animate color change of the indicator (smooth color morphing)
                    var oldColor = (LockIndicatorRectangle.Fill as SolidColorBrush)?.Color ?? systemColor;
                    var targetColor = GetShiftedColor(systemColor, langCode, name);

                    var colorAnim = new ColorAnimation
                    {
                        From = oldColor,
                        To = targetColor,
                        Duration = TimeSpan.FromMilliseconds(250),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    var brush = new SolidColorBrush(oldColor);
                    LockIndicatorRectangle.Fill = brush;
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);

                    // 5. Fade the content back in
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
                // Transition was cancelled by a newer keypress - do nothing!
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