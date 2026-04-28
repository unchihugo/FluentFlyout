// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using MicaWPF.Controls;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static FluentFlyoutWPF.Classes.Utils.MonitorUtil;

namespace FluentFlyoutWPF.Windows;

/// <summary>
/// Interaction logic for LanguageWindow.xaml
/// </summary>
public partial class LanguageWindow : MicaWindow
{
    private CancellationTokenSource cts = new();
    private MainWindow _mainWindow = (MainWindow)Application.Current.MainWindow;
    private bool _isHiding = true;
    private MonitorInfo _openedMonitor;

    public LanguageWindow()
    {
        DataContext = SettingsManager.Current;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);
        CustomWindowChrome.CaptionHeight = 0;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Top = -9999; // start off-screen
        Width = 160;
        Left = (SystemParameters.WorkArea.Width - Width) / 2;
    }

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

        await Dispatcher.InvokeAsync(() =>
        {
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
                LangShortText.Text = langCode.ToUpper();
                
                string name = culture.NativeName;
                if (!SettingsManager.Current.LanguageFlyoutShowRegion)
                {
                    int parenIndex = name.IndexOf('(');
                    if (parenIndex > 0) name = name.Substring(0, parenIndex).Trim();
                }
                if (!string.IsNullOrEmpty(name)) name = char.ToUpper(name[0]) + name.Substring(1);
                LangFullText.Text = name;

                // Apply color
                object colorObj = Application.Current.TryFindResource("MicaWPF.Colors.AccentFillColorDefault");
                Color systemColor = colorObj is Color c ? c : ((SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.AccentFillColorDefault")).Color;
                AccentIndicator.Fill = new SolidColorBrush(GetShiftedColor(systemColor, langCode, name));

                // Force measurement of the content to get the required width before rendering
                ContentStack.Measure(new Size(double.PositiveInfinity, 50));
                double targetWidth = Math.Max(160, ContentStack.DesiredSize.Width + 60);

                var monitor = GetSelectedMonitor(SettingsManager.Current.FlyoutSelectedMonitor);
                double newRawWidth = Math.Ceiling(targetWidth * monitor.dpiX / 96.0);
                double newLeft = Math.Ceiling(monitor.workArea.Left + (monitor.workArea.Width / 2) - (newRawWidth / 2));

                if (_isHiding)
                {
                    _isHiding = false;
                    Width = targetWidth;
                    Left = newLeft * 96.0 / monitor.dpiX;
                    _openedMonitor = monitor;
                    _mainWindow.OpenAnimation(window: this, alwaysBottom: true, selectedMonitor: _openedMonitor);
                }
                else
                {
                    // BEAUTIFUL TRANSITION: Animate width and center position together
                    DoubleAnimation widthAnim = new DoubleAnimation
                    {
                        To = targetWidth,
                        Duration = TimeSpan.FromMilliseconds(75),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    DoubleAnimation leftAnim = new DoubleAnimation
                    {
                        To = newLeft * 96.0 / monitor.dpiX,
                        Duration = TimeSpan.FromMilliseconds(75),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    this.BeginAnimation(Window.WidthProperty, widthAnim);
                    this.BeginAnimation(Window.LeftProperty, leftAnim);
                }
            }
            catch
            {
                LangShortText.Text = "??";
                LangFullText.Text = "Language";
                if (_isHiding)
                {
                    _isHiding = false;
                    Width = 160;
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
