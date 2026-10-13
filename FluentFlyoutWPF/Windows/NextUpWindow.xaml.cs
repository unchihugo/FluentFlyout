// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using MicaWPF.Controls;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.Windows;

/// <summary>
/// Interaction logic for NextUpWindow.xaml
/// </summary>
public partial class NextUpWindow : MicaWindow
{
    private const double MaxWindowWidth = 400;
    private const double MinWindowWidth = 160;
    private const int TextPadding = 8;

    MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
    public NextUpWindow(string title, string artist, BitmapImage thumbnail)
    {
        DataContext = SettingsManager.Current;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -Width - 9999; // move window out of bounds to prevent flickering, maybe needs better solution
        Top = 9999;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);
        CustomWindowChrome.CaptionHeight = 0;

        if (SettingsManager.Current.NextUpAcrylicWindowEnabled)
        {
            WindowBlurHelper.EnableBlur(this);
        }
        else
        {
            WindowBlurHelper.DisableBlur(this);
        }

        ApplyCustomization();
        Width = GetWindowWidth(title, artist);
        SongTitle.Text = title;
        SongArtist.Text = artist;
        UpdateThumbnail(thumbnail);
        Show();

        mainWindow.OpenAnimation(this, positionOverride: SettingsManager.Current.NextUpPosition);

        async void wait()
        {
            await Task.Delay(SettingsManager.Current.NextUpDuration);
            mainWindow.CloseAnimation(this);
            await Task.Delay(MainWindow.getDuration());
            Close();
        }

        wait();
    }

    private void ApplyCustomization()
    {
        var settings = SettingsManager.Current;

        UpNextStackPanel.Visibility = settings.NextUpShowUpNextText ? Visibility.Visible : Visibility.Collapsed;
        SongImageBorder.Margin = settings.NextUpShowUpNextText
            ? new Thickness(12, 1, 0, 0)
            : new Thickness(0, 1, 0, 0);

        var textAlignment = settings.NextUpCenterTitleArtist ? TextAlignment.Center : TextAlignment.Left;
        SongTitle.TextAlignment = textAlignment;
        SongArtist.TextAlignment = textAlignment;
    }

    private double GetWindowWidth(string title, string artist)
    {
        double leadingWidth = 0;

        if (SettingsManager.Current.NextUpShowUpNextText)
        {
            const double iconWidth = 14;
            const double iconRightMargin = 2;
            const double thumbnailLeftMargin = 12;
            leadingWidth = iconWidth
                + iconRightMargin
                + StringWidth.GetStringWidth(UpNextTextBlock.Text, fontSize: 12)
                + thumbnailLeftMargin;
        }

        const double windowHorizontalMargin = 24;
        const double thumbnailWidth = 38;
        const double songInfoLeftMargin = 6;

        double songTextWidth = Math.Max(StringWidth.GetStringWidth(title), StringWidth.GetStringWidth(artist)) + TextPadding;
        double width = windowHorizontalMargin + leadingWidth + thumbnailWidth + songInfoLeftMargin + songTextWidth;

        return Math.Min(Math.Max(width, MinWindowWidth), MaxWindowWidth);
    }

    public void UpdateThumbnail(BitmapImage thumbnail)
    {
        SongImage.ImageSource = thumbnail;
        if (SongImage.ImageSource == null) SongImagePlaceholder.Visibility = Visibility.Visible;
        else SongImagePlaceholder.Visibility = Visibility.Collapsed;
    }
}