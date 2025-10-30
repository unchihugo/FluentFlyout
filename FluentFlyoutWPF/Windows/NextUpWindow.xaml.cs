using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using MicaWPF.Controls;

namespace FluentFlyoutWPF.Windows;

/// <summary>
/// Interaction logic for NextUpWindow.xaml
/// </summary>
public partial class NextUpWindow : MicaWindow
{
    MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
    public NextUpWindow(string title, string artist, BitmapImage thumbnail)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -Width - 9999; // move window out of bounds to prevent flickering, maybe needs better solution
        Top = 9999;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);
        CustomWindowChrome.CaptionHeight = 0;
        CustomWindowChrome.UseAeroCaptionButtons = false;
        CustomWindowChrome.GlassFrameThickness = new Thickness(0);

        var titleWidth = GetStringWidth(title);
        var artistWidth = GetStringWidth(artist);

        if (titleWidth > artistWidth) Width = titleWidth + 142;
        else Width = artistWidth + 142;
        if (Width > 400) Width = 400; // max width to prevent window from being too wide
        SongTitle.Text = title;
        SongArtist.Text = artist;
        SongImage.ImageSource = thumbnail;
        if (SongImage.ImageSource == null) SongImagePlaceholder.Visibility = Visibility.Visible;
        else SongImagePlaceholder.Visibility = Visibility.Collapsed;
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

    private double GetStringWidth(string text)
    {
        var fontFamily = new FontFamily("Segoe UI Variable, Microsoft YaHei, Microsoft JhengHei, MS Gothic");
        var typeface = new Typeface(fontFamily, new FontStyle(), FontWeights.Medium, FontStretches.Normal);

        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            14,
            Brushes.Black,
            null,
            1);

        return formattedText.Width + 8;
    }
}