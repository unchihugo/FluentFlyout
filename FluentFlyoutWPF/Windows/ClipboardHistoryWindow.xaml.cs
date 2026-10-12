// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using MicaWPF.Controls;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using WindowsClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF.Windows;

public partial class ClipboardHistoryWindow : MicaWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly MainWindow _mainWindow;
    private bool _isOpen;
    private bool _isAnimating;
    private bool _expandDownward;
    private int _animationGeneration;
    private IntPtr _returnFocusWindow;

    public ClipboardHistoryWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        DataContext = SettingsManager.Current;
        InitializeComponent();

        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        WindowHelper.SetTopmost(this);
    }

    private DateTime _lastCloseTime = DateTime.MinValue;

    public async void ToggleFromTaskbar(Rect anchorRect, Rect taskbarRect, IntPtr returnFocusWindow)
    {
        try
        {
            if ((DateTime.UtcNow - _lastCloseTime).TotalMilliseconds < 150)
            {
                return;
            }

            if (_isOpen && !_isAnimating)
            {
                await HideAnimatedAsync();
                return;
            }

            if (_isAnimating)
            {
                _isOpen = false;
                _isAnimating = false;
                Hide();
                return;
            }

            _returnFocusWindow = returnFocusWindow;
            _isOpen = true;
            _animationGeneration++;
            await LoadHistoryAsync();
            PositionAtTaskbarAnchor(anchorRect, taskbarRect);
            AnimateOpen();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open clipboard history window");
            _isOpen = false;
            _isAnimating = false;
            Hide();
        }
    }

    private async Task LoadHistoryAsync()
    {
        int generation = _animationGeneration;

        try
        {
            ClipboardHistoryItemsResult result = await WindowsClipboard.GetHistoryItemsAsync();
            if (result.Status != ClipboardHistoryItemsResultStatus.Success)
            {
                string resource = result.Status == ClipboardHistoryItemsResultStatus.ClipboardHistoryDisabled
                    ? "ClipboardHistoryDisabledText"
                    : "ClipboardHistoryUnavailableText";
                StatusText.SetResourceReference(TextBlock.TextProperty, resource);
                return;
            }

            var itemsData = await Task.Run(async () =>
            {
                List<(ClipboardHistoryItem Item, string Preview, string Timestamp, RandomAccessStreamReference? BitmapRef)> list = [];
                foreach (ClipboardHistoryItem item in result.Items)
                {
                    string preview = "";
                    RandomAccessStreamReference? bitmapRef = null;
                    try
                    {
                        if (item.Content.Contains(StandardDataFormats.Text))
                        {
                            string text = await item.Content.GetTextAsync().AsTask();
                            preview = string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
                            if (preview.Length > 240)
                                preview = preview[..240] + "…";
                        }
                        else if (item.Content.Contains(StandardDataFormats.Bitmap))
                        {
                            bitmapRef = await item.Content.GetBitmapAsync().AsTask();
                        }
                    }
                    catch { }

                    list.Add((item, preview, item.Timestamp.LocalDateTime.ToString("g"), bitmapRef));
                }
                return list;
            });

            if (generation != _animationGeneration) return;

            List<ClipboardEntry> entries = [];
            foreach (var data in itemsData)
            {
                string preview = data.Preview;
                ImageSource? image = null;

                if (data.BitmapRef != null)
                {
                    preview = GetResourceString("ClipboardHistoryImageText");
                    try
                    {
                        using var randomAccessStream = await data.BitmapRef.OpenReadAsync();
                        using Stream stream = randomAccessStream.AsStreamForRead();
                        BitmapImage bitmap = new();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 320;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        image = bitmap;
                    }
                    catch { }
                }
                else if (string.IsNullOrEmpty(preview))
                {
                    preview = GetResourceString("ClipboardHistoryOtherContentText");
                }

                entries.Add(new ClipboardEntry(
                    data.Item,
                    string.IsNullOrWhiteSpace(preview) ? GetResourceString("ClipboardHistoryEmptyText") : preview,
                    data.Timestamp,
                    image));
            }

            if (generation != _animationGeneration) return;

            HistoryItems.ItemsSource = entries;
            StatusText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (entries.Count == 0)
                StatusText.SetResourceReference(TextBlock.TextProperty, "ClipboardHistoryEmptyHistoryText");
            HistoryScrollViewer.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearHistoryButton.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            StatusText.SetResourceReference(TextBlock.TextProperty, "ClipboardHistoryUnavailableText");
        }
    }

    private static async Task<ImageSource?> LoadBitmapPreviewAsync(ClipboardHistoryItem item)
    {
        try
        {
            var bitmapReference = await item.Content.GetBitmapAsync();
            using var randomAccessStream = await bitmapReference.OpenReadAsync();
            using Stream stream = randomAccessStream.AsStreamForRead();

            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 320;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string GetResourceString(string key)
    {
        return Application.Current.TryFindResource(key) as string ?? key;
    }

    private void PositionAtTaskbarAnchor(Rect anchorRect, Rect taskbarRect)
    {
        var monitor = MonitorUtil.GetSelectedMonitor(SettingsManager.Current.TaskbarWidgetSelectedMonitor);
        Rect monitorArea = monitor.monitorArea;
        double dpiScale = monitor.dpiY / 96.0;
        double physicalWidth = Width * dpiScale;
        double physicalHeight = Height * dpiScale;
        const double gap = 8;
        const double edgeMargin = 8;

        bool horizontalTaskbar = taskbarRect.Width >= taskbarRect.Height;
        double left;
        double top;

        if (horizontalTaskbar)
        {
            bool taskbarAtBottom = taskbarRect.Top + taskbarRect.Height / 2
                >= monitorArea.Top + monitorArea.Height / 2;
            _expandDownward = !taskbarAtBottom;
            left = anchorRect.Left + anchorRect.Width / 2 - physicalWidth / 2;
            top = taskbarAtBottom
                ? taskbarRect.Top - physicalHeight - gap
                : taskbarRect.Bottom + gap;
        }
        else
        {
            bool taskbarAtRight = taskbarRect.Left + taskbarRect.Width / 2
                >= monitorArea.Left + monitorArea.Width / 2;
            _expandDownward = anchorRect.Top + anchorRect.Height / 2
                < monitorArea.Top + monitorArea.Height / 2;
            left = taskbarAtRight
                ? taskbarRect.Left - physicalWidth - gap
                : taskbarRect.Right + gap;
            top = anchorRect.Top + anchorRect.Height / 2 - physicalHeight / 2;
        }

        left = Math.Clamp(left, monitorArea.Left + edgeMargin, monitorArea.Right - physicalWidth - edgeMargin);
        top = Math.Clamp(top, monitorArea.Top + edgeMargin, monitorArea.Bottom - physicalHeight - edgeMargin);
        WindowHelper.SetPosition(this, left, top);
    }

    private void AnimateOpen()
    {
        int generation = _animationGeneration;
        int duration = MainWindow.getDuration();
        double dpiScale = MonitorUtil.GetSelectedMonitor(SettingsManager.Current.TaskbarWidgetSelectedMonitor).dpiY / 96.0;
        Rect placement = WindowHelper.GetPlacement(this);
        double targetTop = placement.Top / dpiScale;
        double offset = (_expandDownward ? -20 : 20) / dpiScale;

        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);
        Top = targetTop;
        Opacity = duration == 0 ? 1 : 0;
        Show();
        WindowHelper.SetTopmost(this);
        Activate();

        if (duration == 0)
            return;

        _isAnimating = true;
        EasingFunctionBase? easing = SettingsManager.Current.FlyoutAnimationEasingStyle == 0
            ? null
            : _mainWindow.getEasingStyle(true);
        DoubleAnimation move = new(targetTop + offset, targetTop, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = easing
        };
        DoubleAnimation fade = new(0, 1, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = easing
        };
        fade.Completed += (_, _) =>
        {
            if (generation != _animationGeneration)
                return;
            _isAnimating = false;
            BeginAnimation(TopProperty, null);
            BeginAnimation(OpacityProperty, null);
            Top = targetTop;
            Opacity = 1;
        };
        BeginAnimation(TopProperty, move);
        BeginAnimation(OpacityProperty, fade);
    }

    private async Task HideAnimatedAsync()
    {
        _lastCloseTime = DateTime.UtcNow;
        if (!_isOpen && !_isAnimating)
            return;

        _isOpen = false;
        int generation = ++_animationGeneration;
        int duration = MainWindow.getDuration();
        if (duration == 0)
        {
            _isAnimating = false;
            Hide();
            return;
        }

        _isAnimating = true;
        double offset = (_expandDownward ? -20 : 20)
            / (MonitorUtil.GetSelectedMonitor(SettingsManager.Current.TaskbarWidgetSelectedMonitor).dpiY / 96.0);
        double currentTop = Top;
        EasingFunctionBase? easing = SettingsManager.Current.FlyoutAnimationEasingStyle == 0
            ? null
            : _mainWindow.getEasingStyle(false);
        BeginAnimation(TopProperty, new DoubleAnimation(currentTop, currentTop + offset, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = easing
        });
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = easing
        });

        await Task.Delay(duration);
        if (generation != _animationGeneration)
            return;

        _isAnimating = false;
        Hide();
        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
    }

    private async void HistoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ClipboardEntry entry })
            return;

        SetHistoryItemAsContentStatus status = WindowsClipboard.SetHistoryItemAsContent(entry.Item);
        if (status != SetHistoryItemAsContentStatus.Success)
            return;

        await HideAnimatedAsync();

        if (_returnFocusWindow != IntPtr.Zero && SetForegroundWindow(_returnFocusWindow))
        {
            await Task.Delay(50);
            const byte vkControl = 0x11;
            const byte vkV = 0x56;
            const uint keyUp = 0x0002;

            keybd_event(vkControl, 0, 0, IntPtr.Zero);
            keybd_event(vkV, 0, 0, IntPtr.Zero);
            keybd_event(vkV, 0, keyUp, IntPtr.Zero);
            keybd_event(vkControl, 0, keyUp, IntPtr.Zero);
        }
    }

    private async void DeleteHistoryItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: ClipboardEntry entry }
            && WindowsClipboard.DeleteItemFromHistory(entry.Item))
        {
            await LoadHistoryAsync();
        }
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowsClipboard.ClearHistory())
            await LoadHistoryAsync();
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        await HideAnimatedAsync();
    }

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_isOpen && !_isAnimating)
            await HideAnimatedAsync();
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            await HideAnimatedAsync();
    }

    private sealed record ClipboardEntry(
        ClipboardHistoryItem Item,
        string Preview,
        string Timestamp,
        ImageSource? Image);
}
