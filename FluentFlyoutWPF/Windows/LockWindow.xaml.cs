using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using MicaWPF.Controls;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Wpf.Ui.Controls;


namespace FluentFlyoutWPF.Windows;

/// <summary>
/// Interaction logic for LockWindow.xaml
/// </summary>
public partial class LockWindow : MicaWindow
{
    private CancellationTokenSource cts;
    MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
    private bool _isHiding = true;

    public LockWindow()
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);
        CustomWindowChrome.CaptionHeight = 0;
        CustomWindowChrome.UseAeroCaptionButtons = false;
        CustomWindowChrome.GlassFrameThickness = new Thickness(0);

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

            LockTextBlock.FontWeight = SettingsManager.Current.LockKeysBoldUi ? FontWeights.Bold : FontWeights.Normal;
            double duration = mainWindow.getDuration();
            if (duration == 0)
            {
                LockSymbol.BeginAnimation(OpacityProperty, null);
                UnlockSymbol.BeginAnimation(OpacityProperty, null);
                LockIndicatorRectangle.BeginAnimation(OpacityProperty, null);
                LockIndicatorRectangle.BeginAnimation(WidthProperty, null);
                LockIconBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
                UnlockIconBlur.BeginAnimation(BlurEffect.RadiusProperty, null);

                LockIconBlur.Radius = 0;
                UnlockIconBlur.Radius = 0;

                if (isOn)
                {
                    LockIndicatorRectangle.Opacity = 1;
                    LockIndicatorRectangle.Width = 80;
                    if (SettingsManager.Current.LockKeysBoldUi) LockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockClosed24;
                    else LockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockClosed20;
                    LockSymbol.Opacity = 1;
                    UnlockSymbol.Opacity = 0;
                }
                else
                {
                    LockIndicatorRectangle.Opacity = 0.2;
                    LockIndicatorRectangle.Width = 60;
                    if (SettingsManager.Current.LockKeysBoldUi) UnlockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockOpen24;
                    else UnlockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockOpen20;
                    LockSymbol.Opacity = 0;
                    UnlockSymbol.Opacity = 1;
                }
            }
            else 
            {
                PlaySwitchAnimation(isOn, duration / 300);
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
        if (LocalizationManager.LanguageCode == "es")
        {
            Width = 300; // default width x1.5
        }
        else
        {
            Width = 200; // default width
        }

        setStatus(key, isOn);

        if (_isHiding)
        {
            _isHiding = false;
            mainWindow.OpenAnimation(this, true);
            double duration = mainWindow.getDuration();
            if (duration == 0)
            {
                LockIconTrans.X = -80;
                LockIconBlur.Radius = 0;
                UnlockIconTrans.X = -80;
                UnlockIconBlur.Radius = 0;

            }
            else
            {
                LockIconTrans.BeginAnimation(TranslateTransform.XProperty, null);
                UnlockIconTrans.BeginAnimation(TranslateTransform.XProperty, null);
                LockTextBlock.BeginAnimation(OpacityProperty, null);
                LockIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                LockIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                UnlockIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                UnlockIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);


                LockTextBlock.Opacity = 0;
                LockIconTrans.X = 0;
                UnlockIconTrans.X = 0;
                LockIconScale.ScaleX = 1;
                LockIconScale.ScaleY = 1;
                UnlockIconScale.ScaleX = 1;
                UnlockIconScale.ScaleY = 1;

                PlayEntranceAnimation(isOn, duration / 300);

            }

        }
        cts.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        Visibility = Visibility.Visible;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(SettingsManager.Current.LockKeysDuration, token);
                mainWindow.CloseAnimation(this, true);
                _isHiding = true;
                await Task.Delay(mainWindow.getDuration());
                if (_isHiding == false) return;
                Hide();
                break;
            }
        }
        catch (TaskCanceledException)
        {
            // do nothing
        }
    }
    private void PlaySwitchAnimation(bool isOn, double durationRatio)
    {
        var storyboard = new Storyboard();

        void AddAnim(string targetName, string property, double? from, double to, double durationSec, double beginSec = 0, IEasingFunction? easing = null)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(durationSec * durationRatio),
                BeginTime = TimeSpan.FromSeconds(beginSec),
                EasingFunction = easing
            };
            Storyboard.SetTargetName(anim, targetName);
            Storyboard.SetTargetProperty(anim, new PropertyPath(property));
            storyboard.Children.Add(anim);
        }

        if (isOn)
        {
            var StartTimePoint1 = 0.1 * durationRatio;
            AddAnim("LockIndicatorRectangle", "Width", null, 80, 0.4, StartTimePoint1, new BackEase { EasingMode = EasingMode.EaseOut });
            AddAnim("LockIndicatorRectangle", "Opacity", null, 1, 0.4, StartTimePoint1, null);
            AddAnim("UnlockIconBlur", "Radius",null , 8, 0.4, StartTimePoint1, null);
            AddAnim("UnlockSymbol", "Opacity",1 , 0, 0.5, StartTimePoint1, null);
            //AddAnim("UnlockIconScale", "ScaleX", 1, 1.5, 0.3, StartTimePoint1, new CircleEase { EasingMode = EasingMode.EaseOut });
            //AddAnim("UnlockIconScale", "ScaleY", 1,1.5, 0.3, StartTimePoint1, new CircleEase { EasingMode = EasingMode.EaseOut });
            AddAnim("LockIconBlur", "Radius",8 , 0, 0.5, StartTimePoint1, null);
            AddAnim("LockSymbol", "Opacity",null , 1, 0.4, StartTimePoint1, null);
            //AddAnim("LockIconScale", "ScaleX", 1.5, 1, 0.3, StartTimePoint2, new CircleEase { EasingMode = EasingMode.EaseOut });
            //AddAnim("LockIconScale", "ScaleY", 1.5, 1, 0.3, StartTimePoint2, new CircleEase { EasingMode = EasingMode.EaseOut });

                

        }
        else  //isOff
        {
            var StartTimePoint1 = 0.1 * durationRatio;
            AddAnim("LockIndicatorRectangle", "Width", null, 60, 0.4, StartTimePoint1, new BackEase { EasingMode = EasingMode.EaseOut });
            AddAnim("LockIndicatorRectangle", "Opacity", null, 0.2, 0.4, StartTimePoint1, null);
            AddAnim("LockIconBlur", "Radius", null, 8, 0.4, StartTimePoint1, null);
            AddAnim("LockSymbol", "Opacity", 1, 0, 0.5, StartTimePoint1, null);
            //AddAnim("LockIconScale", "ScaleX", 1, 1.5, 0.3, StartTimePoint1, new CircleEase { EasingMode = EasingMode.EaseOut });
            //AddAnim("LockIconScale", "ScaleY", 1, 1.5, 0.3, StartTimePoint1, new CircleEase { EasingMode = EasingMode.EaseOut });
            AddAnim("UnlockIconBlur", "Radius", 8, 0, 0.5, StartTimePoint1, null);
            AddAnim("UnlockSymbol", "Opacity", null, 1, 0.4, StartTimePoint1, null);
            //AddAnim("UnlockIconScale", "ScaleX", 1.5, 1, 0.3, StartTimePoint2, new CircleEase { EasingMode = EasingMode.EaseOut });
            //AddAnim("UnlockIconScale", "ScaleY", 1.5, 1, 0.3, StartTimePoint2, new CircleEase { EasingMode = EasingMode.EaseOut });
        }


        storyboard.Begin(this);

    }

    private void PlayEntranceAnimation(bool isOn, double durationRatio)
    {
        var storyboard = new Storyboard();

        void AddAnim(string targetName, string property, double? from, double to, double durationSec, double beginSec = 0, IEasingFunction? easing = null)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(durationSec * durationRatio),
                BeginTime = TimeSpan.FromSeconds(beginSec),
                EasingFunction = easing
            };
            Storyboard.SetTargetName(anim, targetName);
            Storyboard.SetTargetProperty(anim, new PropertyPath(property));
            storyboard.Children.Add(anim);
        }
        var StartTimePoint1 = 0.6 * durationRatio;
        AddAnim("LockIconTrans", "X", 0, -80, 1, StartTimePoint1, new CubicEase { EasingMode = EasingMode.EaseInOut });
        AddAnim("UnlockIconTrans", "X", 0, -80, 1, StartTimePoint1, new CubicEase { EasingMode = EasingMode.EaseInOut });
        AddAnim("LockTextBlock", "Opacity", 0, 1, 0.5, StartTimePoint1+0.4, null);
        AddAnim("TextBlockBlur", "Radius", 8, 0, 0.5, StartTimePoint1+0.4, null);
        AddAnim("TextBlockTrans", "X", 50, 0, 0.5, StartTimePoint1+0.4, new CubicEase { EasingMode = EasingMode.EaseOut });
        AddAnim("UnlockIconScale", "ScaleX", 1, 0.8, 0.3, StartTimePoint1+0.3, new CubicEase { EasingMode = EasingMode.EaseOut });
        AddAnim("UnlockIconScale", "ScaleY", 1, 0.8, 0.3, StartTimePoint1+0.3, new CubicEase { EasingMode = EasingMode.EaseOut });
        AddAnim("LockIconScale", "ScaleX", 1, 0.8, 0.5, StartTimePoint1+0.3, new CubicEase { EasingMode = EasingMode.EaseOut });
        AddAnim("LockIconScale", "ScaleY", 1, 0.8, 0.5, StartTimePoint1+0.3, new CubicEase { EasingMode = EasingMode.EaseOut });


        //AddAnim("TextBlockScale", "ScaleX", 0.5, 1, 0.5, StartTimePoint1 + 0.1, new CubicEase { EasingMode = EasingMode.EaseOut });
        //AddAnim("TextBlockScale", "ScaleY", 0.5, 1, 0.5, StartTimePoint1 + 0.1, new CubicEase { EasingMode = EasingMode.EaseOut });

        storyboard.Begin(this);
    } 
    }