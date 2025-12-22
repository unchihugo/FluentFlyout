using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using MicaWPF.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

        var titleWidth = StringWidth.GetStringWidth(title);
        var artistWidth = StringWidth.GetStringWidth(artist);
        string? UpNextText = Application.Current.FindResource("NextUpWindow_UpNextText") as string;
        var UpNextTitleWidth = StringWidth.GetStringWidth(UpNextText) + 14;
        var maxContentWidth = titleWidth > artistWidth ? titleWidth : artistWidth;
        Width = maxContentWidth + UpNextTitleWidth + 86;
        if (Width > 400) Width = 400; // max width to prevent window from being too wide
        double titleTransX = -(Width / 2) + (UpNextTitleWidth / 2) + 12;
        double backgroundTransX = -(Width / 2);
        TitleColumn.Width = new GridLength(UpNextTitleWidth);
        SongTitle.Text = title;
        SongArtist.Text = artist;
        SongImage.ImageSource = thumbnail;
        if (SongImage.ImageSource == null)
            SongImagePlaceholder.Visibility = Visibility.Visible;
        else
        {
            SongImagePlaceholder.Visibility = Visibility.Collapsed;
            BackgroundGradientStopColor.Color = ImageHelper.GetDominantColor(thumbnail);
        }
        Show();
        mainWindow.OpenAnimation(this);
        double duration = mainWindow.getDuration();
        if(duration==0)
        {
            BackgroundBorder.Opacity = 0;
            ImageScale.ScaleX = 1;
            ImageScale.ScaleY = 1;
            ImageBlur.Radius = 0;
            SongInfoStackPanel.Opacity = 1;
            SongImageBorder.Opacity = 1;
            InfoScale.ScaleX = 1;
            InfoScale.ScaleY = 1;
            InfoBlur.Radius = 0;
            NextTitle.Opacity = 1;
            TitleTranslate.X = titleTransX;
            TitleScale.ScaleX = 1;
            TitleScale.ScaleY = 1;
            TitleBlur.Radius = 0;
        }
        else 
        {
            BackgroundBorder.Opacity = 1;
            ImageScale.ScaleX = 0.3;
            ImageScale.ScaleY = 0.3;
            ImageBlur.Radius = 6;
            SongInfoStackPanel.Opacity = 0;
            SongImageBorder.Opacity = 0;
            InfoScale.ScaleX = 0.3;
            InfoScale.ScaleY = 0.3;
            InfoBlur.Radius = 6;
            NextTitle.Opacity = 0;
            TitleTranslate.X = 0;
            TitleScale.ScaleX = 0.3;
            TitleScale.ScaleY = 0.3;
            TitleBlur.Radius = 6;
            PlayEntranceAnimation(titleTransX, backgroundTransX,duration/300);
        }



        async void wait()
        {
            await Task.Delay(SettingsManager.Current.NextUpDuration);
            mainWindow.CloseAnimation(this);
            await Task.Delay(mainWindow.getDuration());
            Close();
        }

        wait();
    }
    private void PlayEntranceAnimation(double titleTranslateXTo, double backgroundTranslateXTo, double durationRatio)
    {
        var storyboard = new Storyboard();

        void AddAnim(string targetName, string property, double? from, double to, double durationSec, double beginSec = 0, IEasingFunction? easing = null)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(durationSec*durationRatio),
                BeginTime = TimeSpan.FromSeconds(beginSec),
                EasingFunction = easing
            };
            Storyboard.SetTargetName(anim, targetName);
            Storyboard.SetTargetProperty(anim, new PropertyPath(property));
            storyboard.Children.Add(anim);
        }

        // Title Y
        AddAnim("TitleTranslate", "Y", 30, -2, 0.9, 0, new CircleEase { EasingMode = EasingMode.EaseOut });
        // Title Scale
        AddAnim("TitleScale", "ScaleX", null, 1.3, 0.7, 0, new CubicEase { EasingMode = EasingMode.EaseOut });
        AddAnim("TitleScale", "ScaleY", null, 1.3, 0.7, 0, new CubicEase { EasingMode = EasingMode.EaseOut });
        // NextTitle fade in
        AddAnim("NextTitle", "Opacity", 0, 1, 0.5, 0);
        // Title blur
        var startTimePoint1 = 0;
        AddAnim("TitleBlur", "Radius", 8, 0, 0.5, startTimePoint1*durationRatio, new CubicEase { EasingMode = EasingMode.EaseOut });

        // Background
        AddAnim("BackgroundTranslate", "Y", 20, 0, 0.6, 0, new CubicEase { EasingMode = EasingMode.EaseOut });
        AddAnim("BackgroundScale", "ScaleX", 0, 1, 0.8, 0, new CubicEase { EasingMode = EasingMode.EaseOut });
        AddAnim("BackgroundScale", "ScaleY", 0, 1, 0.8, 0, new CubicEase { EasingMode = EasingMode.EaseOut });
        AddAnim("BackgroundBorder", "Opacity", 0, 1, 0.8, 0);
        var startTimePoint2 = 0.5*durationRatio;
        AddAnim("BackgroundBorder", "Opacity", null, 0, 2, startTimePoint2); // fade out
        AddAnim("BackgroundTranslate", "X", 0, backgroundTranslateXTo, 1.2,startTimePoint2, new CubicEase { EasingMode = EasingMode.EaseIn });
       
        var startTimePoint3 = 0.6 * durationRatio;
        // Title scale back
        AddAnim("TitleScale", "ScaleX", null, 1, 0.5, startTimePoint3, new CubicEase { EasingMode = EasingMode.EaseOut });
        AddAnim("TitleScale", "ScaleY", null, 1, 0.5, startTimePoint3, new CubicEase { EasingMode = EasingMode.EaseOut });

        // Title Y back to 0
        AddAnim("TitleTranslate", "Y", null, 0, 1, startTimePoint3, new CubicEase { EasingMode = EasingMode.EaseInOut });

        // Note: Original had TitleTranslate.X animation with no From/To → skip (no effect)
        AddAnim("TitleTranslate", "X", 0, titleTranslateXTo, 1.1, startTimePoint3, new CubicEase { EasingMode = EasingMode.EaseInOut });


        // Image
        var startTimePoint4 = 0.9;
        AddAnim("SongImageBorder", "Opacity", null, 1, 0.5, startTimePoint4*durationRatio);
        AddAnim("ImageTranslate", "X", 40, 0, 0.7, startTimePoint4*durationRatio, new CircleEase { EasingMode = EasingMode.EaseOut });
        AddAnim("ImageScale", "ScaleX", 0.5, 1, 0.5, startTimePoint4 * durationRatio, new CircleEase { EasingMode = EasingMode.EaseOut });
        AddAnim("ImageScale", "ScaleY", 0.5, 1, 0.5, startTimePoint4 * durationRatio, new CircleEase { EasingMode = EasingMode.EaseOut });
        AddAnim("ImageBlur", "Radius", 8, 0, 0.7, startTimePoint4 * durationRatio);

        // Info
        var startTimePoint5 = 1;
        AddAnim("SongInfoStackPanel", "Opacity", null, 1, 0.5, startTimePoint5*durationRatio);
        AddAnim("InfoTranslate", "X", 40, 0, 0.7, startTimePoint5 * durationRatio, new CircleEase { EasingMode = EasingMode.EaseOut });
        AddAnim("InfoScale", "ScaleX", 0.5, 1, 0.5, startTimePoint5 * durationRatio, new CircleEase { EasingMode = EasingMode.EaseOut });
        AddAnim("InfoScale", "ScaleY", 0.5, 1, 0.5, startTimePoint5 * durationRatio, new CircleEase { EasingMode = EasingMode.EaseOut });
        AddAnim("InfoBlur", "Radius", 8, 0, 0.7, startTimePoint5 * durationRatio);
        storyboard.Begin(this);

    }
    public void UpdateThumbnail(BitmapImage thumbnail)
    {
        SongImage.ImageSource = thumbnail;
        if (SongImage.ImageSource == null) SongImagePlaceholder.Visibility = Visibility.Visible;
        else SongImagePlaceholder.Visibility = Visibility.Collapsed;
    }
}