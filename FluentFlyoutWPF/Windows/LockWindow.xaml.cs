// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using MicaWPF.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static FluentFlyoutWPF.Classes.Utils.MonitorUtil;


namespace FluentFlyoutWPF.Windows;

/// <summary>
/// Interaction logic for LockWindow.xaml
/// </summary>
public partial class LockWindow : MicaWindow
{
    private CancellationTokenSource cts;
    private MainWindow _mainWindow = (MainWindow)Application.Current.MainWindow;
    private bool _isHiding = true;
    private MonitorInfo _openedMonitor;

    public LockWindow()
    {
        DataContext = SettingsManager.Current;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);

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

            if (isOn)
            {
                if (SettingsManager.Current.LockKeysBoldUi) LockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockClosed24;
                else LockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockClosed20;
            }
            else
            {
                if (SettingsManager.Current.LockKeysBoldUi) LockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockOpen24;
                else LockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockOpen20;
            }

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
            }
            else
            {
                LockIndicatorRectangle.BeginAnimation(OpacityProperty, null);
                LockIndicatorRectangle.BeginAnimation(WidthProperty, null);

                LockIndicatorRectangle.Opacity = targetOpacity;
                LockIndicatorRectangle.Width = targetWidth;
            }
        });
    }

    public async void ShowLockFlyout(string? key, bool isOn)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (SettingsManager.Current.LockKeysAcrylicWindowEnabled)
        {
            WindowBlurHelper.EnableBlur(this);
        }
        else
        {
            WindowBlurHelper.DisableBlur(this);
        }

        // lengthen the window width to fit longer translated texts
        if (LocalizationManager.LanguageCode != "en")
        {
            Width = LocalizationManager.maxLength + 56.0; //Max length of the text + extra space for the icon and padding
        }
        else
        {
            Width = 160; // default width
        }

        setStatus(key, isOn);

        if (_isHiding)
        {
            _isHiding = false;
            _openedMonitor = GetPreferredTargetDisplay();
            _mainWindow.OpenAnimation(this, true, _openedMonitor);
        }
        cts.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(SettingsManager.Current.LockKeysDuration, token);
                _mainWindow.CloseAnimation(this, true, _openedMonitor);
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