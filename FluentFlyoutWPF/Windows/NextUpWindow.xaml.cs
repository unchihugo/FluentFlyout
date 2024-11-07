using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyoutWPF.Classes;
using FluentFlyout.Properties;
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
        SongTitle.Text = title;
        SongArtist.Text = artist;
        SongImage.ImageSource = thumbnail;
        if (SongImage.ImageSource == null) SongImagePlaceholder.Visibility = Visibility.Visible;
        else SongImagePlaceholder.Visibility = Visibility.Collapsed;
        Show();
        mainWindow.OpenAnimation(this);

        async void wait()
        {
            await Task.Delay(Settings.Default.NextUpDuration);
            mainWindow.CloseAnimation(this);
            await Task.Delay(mainWindow.getDuration());
            Close();
        }

        wait();
    }

    private double GetStringWidth(string text)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI Variable"), new FontStyle(), FontWeights.Medium, FontStretches.Normal);

        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            14,
            Brushes.Black,
            null,
            1);

        return formattedText.Width;
    }
}