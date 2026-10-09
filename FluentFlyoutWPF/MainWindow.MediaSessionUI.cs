// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    private bool _hasMultipleMediaSessions;
    private bool _isMediaSessionMenuOpen;
    private bool _mediaSessionTogglePointerDown;

    private void UpdateMediaSessionSelectorVisibility(bool hasMultipleMediaSessions)
    {
        bool compactLayout = SettingsManager.Current.CompactLayout;
        bool showPlayerInfo = SettingsManager.Current.PlayerInfoEnabled && !compactLayout;

        MediaIdButton.Visibility = showPlayerInfo && !hasMultipleMediaSessions
            ? Visibility.Visible
            : Visibility.Collapsed;
        MediaSessionSplitButton.Visibility = showPlayerInfo && hasMultipleMediaSessions
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactMediaSessionButton.Visibility = hasMultipleMediaSessions &&
                                               (compactLayout || !SettingsManager.Current.PlayerInfoEnabled)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void MediaSessionMenu_Opened(object sender, RoutedEventArgs e)
    {
        _isMediaSessionMenuOpen = true;
        if (!_isCleaningUp)
            cts.Cancel();
        if (sender is ContextMenu menu)
            PopulateSessionSwitcherMenu(menu);
    }

    private void MediaSessionMenu_Closed(object sender, RoutedEventArgs e)
    {
        _isMediaSessionMenuOpen = false;
        if (!_isCleaningUp && IsVisible && !SettingsManager.Current.MediaFlyoutAlwaysDisplay)
            ShowMediaFlyout(forceShow: true, refreshUi: false);
    }

    private static Wpf.Ui.Controls.IconElement CreateMediaPlayerIcon(ImageSource? icon)
    {
        if (icon != null)
        {
            return new Wpf.Ui.Controls.ImageIcon
            {
                Source = icon,
                Width = 16,
                Height = 16
            };
        }

        return new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.AppGeneric20, 16, false);
    }

    private void MediaSessionSplitButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mediaSessionTogglePointerDown =
            MediaSessionSplitButton.Template.FindName("PART_Toggle", MediaSessionSplitButton) is FrameworkElement { IsMouseOver: true };
    }

    private void MediaSessionSplitButton_Click(object sender, RoutedEventArgs e)
    {
        bool toggleIsPointerOver =
            MediaSessionSplitButton.Template.FindName("PART_Toggle", MediaSessionSplitButton) is FrameworkElement { IsMouseOver: true };

        // The nested toggle can also raise Click on the outer button.
        if (!ReferenceEquals(e.Source, sender) || _mediaSessionTogglePointerDown || toggleIsPointerOver)
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(() => _mediaSessionTogglePointerDown = false, DispatcherPriority.Input);
            return;
        }

        _mediaSessionTogglePointerDown = false;
        if (!SettingsManager.Current.PlayerInfoEnabled || SettingsManager.Current.CompactLayout) return;
        e.Handled = true;
        _ = TryOpenMediaPlayerAsync();
    }
}