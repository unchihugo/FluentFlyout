using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using MicaWPF.Controls;
using System.Windows;


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

            LockTextBlock.FontWeight = SettingsManager.Current.LockKeysBoldUi ? FontWeights.Medium : FontWeights.Normal;

            if (isOn)
            {
                LockIndicatorRectangle.Opacity = 1;
                if (SettingsManager.Current.LockKeysBoldUi) LockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockClosed24;
                else LockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockClosed20;
            }
            else
            {
                LockIndicatorRectangle.Opacity = 0.2;
                if (SettingsManager.Current.LockKeysBoldUi) LockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockOpen24;
                else LockSymbol.Symbol = Wpf.Ui.Controls.SymbolRegular.LockOpen20;
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
            Width = 240; // default width x1.5
        }
        else
        {
            Width = 160; // default width
        }

        setStatus(key, isOn);

        if (_isHiding)
        {
            _isHiding = false;
            mainWindow.OpenAnimation(this, true);
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
}