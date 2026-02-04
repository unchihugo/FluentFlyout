// Copyright © 2024-2026 The FluentFlyout Authors
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
        CustomWindowChrome.UseAeroCaptionButtons = false;
        CustomWindowChrome.GlassFrameThickness = new Thickness(0);
        if (SettingsManager.Current.NextUpAcrylicWindowEnabled)
        {
            WindowBlurHelper.EnableBlur(this);
        }
        else
        {
            WindowBlurHelper.DisableBlur(this);
        }

        var upNextWidth = StringWidth.GetStringWidth(UpNextTextBlock.Text);
        var titleWidth = StringWidth.GetStringWidth(title);
        var artistWidth = StringWidth.GetStringWidth(artist);

        if (titleWidth > artistWidth) Width = titleWidth + 76 + upNextWidth;
        else Width = artistWidth + 76 + upNextWidth;
        if (Width > 400) Width = 400; // max width to prevent window from being too wide
        SongTitle.Text = title;
        SongArtist.Text = artist;
        UpdateThumbnail(thumbnail);
        Show();

        mainWindow.OpenAnimation(this);

        async void wait()
        {
            await Task.Delay(SettingsManager.Current.NextUpDuration);
            mainWindow.CloseAnimation(this);
            await Task.Delay(mainWindow.getDuration());
            Close();
        }

        wait();
    }

    public void UpdateThumbnail(BitmapImage thumbnail)
    {
        SongImage.ImageSource = thumbnail;
        if (SongImage.ImageSource == null) SongImagePlaceholder.Visibility = Visibility.Visible;
        else SongImagePlaceholder.Visibility = Visibility.Collapsed;
    }
}