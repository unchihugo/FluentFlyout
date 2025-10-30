using MicaWPF.Controls;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Windows.ApplicationModel;
using MessageBox = System.Windows.MessageBox;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes;


namespace FluentFlyoutWPF;

public partial class SettingsWindow : MicaWindow
{
    private static SettingsWindow? instance; // for singleton

    public SettingsWindow()
    {
        if (instance != null)
        {
            if (instance.WindowState == WindowState.Minimized)
            {
                instance.WindowState = WindowState.Normal;
            }

            instance.Activate();
            instance.Focus();
            Close();
            return;
        }

        InitializeComponent();
        instance = this;

        Closed += (s, e) => instance = null;

        // is there a better way to do this?
        LayoutSwitch.IsChecked = SettingsManager.Current.CompactLayout;
        PositionComboBox.SelectedIndex = SettingsManager.Current.Position;
        FlyoutAnimationSpeedComboBox.SelectedIndex = SettingsManager.Current.FlyoutAnimationSpeed;
        PlayerInfoSwitch.IsChecked = SettingsManager.Current.PlayerInfoEnabled;
        RepeatSwitch.IsChecked = SettingsManager.Current.RepeatEnabled;
        ShuffleSwitch.IsChecked = SettingsManager.Current.ShuffleEnabled;
        StartupSwitch.IsChecked = SettingsManager.Current.Startup;
        DurationTextBox.Text = SettingsManager.Current.Duration.ToString();
        NextUpSwitch.IsChecked = SettingsManager.Current.NextUpEnabled;
        NextUpDurationTextBox.Text = SettingsManager.Current.NextUpDuration.ToString();
        nIconLeftClickComboBox.SelectedIndex = SettingsManager.Current.nIconLeftClick;
        CenterTitleArtistSwitch.IsChecked = SettingsManager.Current.CenterTitleArtist;
        AnimationEasingStylesComboBox.SelectedIndex = SettingsManager.Current.FlyoutAnimationEasingStyle;
        LockKeysSwitch.IsChecked = SettingsManager.Current.LockKeysEnabled;
        LockKeysDurationTextBox.Text = SettingsManager.Current.LockKeysDuration.ToString();
        AppThemeComboBox.SelectedIndex = SettingsManager.Current.AppTheme;
        MediaFlyoutEnabledSwitch.IsChecked = SettingsManager.Current.MediaFlyoutEnabled;
        nIconSymbolSwitch.IsChecked = SettingsManager.Current.nIconSymbol;
        DisableIfFullscreenSwitch.IsChecked = SettingsManager.Current.DisableIfFullscreen;
        LockKeysBoldUISwitch.IsChecked = SettingsManager.Current.LockKeysBoldUI;
        SeekbarSwitch.IsChecked = SettingsManager.Current.SeekbarEnabled;
        PauseOtherSessionsEnabledSwitch.IsChecked = SettingsManager.Current.PauseOtherSessionsEnabled;
        LockKeysEnableInsertSwitch.IsChecked = SettingsManager.Current.LockKeysInsertEnabled;
        BackgroundComboBox.SelectedIndex = SettingsManager.Current.MediaFlyoutBackgroundBlur;
        AcrylicWindowSwitch.IsChecked = SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled;
        MediaFlyoutAlwaysDisplaySwitch.IsChecked = SettingsManager.Current.MediaFlyoutAlwaysDisplay;
        DurationTextBox.IsEnabled = !SettingsManager.Current.MediaFlyoutAlwaysDisplay;

        try // gets the version of the app, works only in release mode
        {
            var version = Package.Current.Id.Version;
            VersionTextBlock.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            VersionTextBlock.Text = "debug version";
        }

        // initialize language dropdown & selection
        try
        {
            foreach (var lang in LocalizationManager.SupportedLanguages)
            {
                var comboBoxItem = new ComboBoxItem
                {
                    Content = lang.Key,
                    Tag = lang.Value
                };
                AppLanguageComboBox.Items.Add(comboBoxItem);
            }

            AppLanguageComboBox.SelectedItem = AppLanguageComboBox.Items
            .Cast<ComboBoxItem>()
            .FirstOrDefault(item => ((string?)item.Tag ?? "system").Equals(SettingsManager.Current.AppLanguage));

            ThemeManager.ApplySavedTheme();
        } 
        catch (Exception ex)
        {
            Debug.WriteLine("Error setting app language: " + ex.Message);
        }
    }

    public static void ShowInstance()
    {
        if (instance == null)
        {
            new SettingsWindow().Show();
        }
        else
        {
            if (instance.WindowState == WindowState.Minimized)
            {
                instance.WindowState = WindowState.Normal;
            }

            instance.Activate();
            instance.Focus();
        }
    }

    private void SettingsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SettingsManager.SaveSettings();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.SaveSettings();
        Close();
    }

    private void LayoutSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.CompactLayout = LayoutSwitch.IsChecked ?? false;
    }

    private void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        // might not work if installed using MSIX, needs investigation
        SetStartup(StartupSwitch.IsChecked ?? false);
        SettingsManager.Current.Startup = StartupSwitch.IsChecked ?? false;
    }

    private void PositionComboBox_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SettingsManager.Current.Position = PositionComboBox.SelectedIndex;
    }

    private void RepeatSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.RepeatEnabled = RepeatSwitch.IsChecked ?? false;
    }

    private void ShuffleSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.ShuffleEnabled = ShuffleSwitch.IsChecked ?? false;
    }

    private void FlyoutAnimationSpeedComboBox_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SettingsManager.Current.FlyoutAnimationSpeed = FlyoutAnimationSpeedComboBox.SelectedIndex;
    }

    private void NextUpSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.NextUpEnabled = NextUpSwitch.IsChecked ?? false;
    }

    private void DurationTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string text = DurationTextBox.Text.Trim();
        string numericText = new string(text.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(numericText))
        {
            DurationTextBox.Text = "0";
            SettingsManager.Current.Duration = 0;
        }
        else
        {
            DurationTextBox.Text = numericText;
            if (int.TryParse(numericText, out int duration))
            {
                if (duration > 10000)
                {
                    duration = 10000;
                }

                DurationTextBox.Text = duration.ToString();
                SettingsManager.Current.Duration = duration;
            }
            else
            {
                DurationTextBox.Text = "3000";
                SettingsManager.Current.Duration = 3000;
            }
        }

        DurationTextBox.CaretIndex = DurationTextBox.Text.Length;
    }

    private void NextUpDurationTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string text = NextUpDurationTextBox.Text.Trim();
        string numericText = new string(text.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(numericText))
        {
            NextUpDurationTextBox.Text = "0";
            SettingsManager.Current.NextUpDuration = 0;
        }
        else
        {
            NextUpDurationTextBox.Text = numericText;
            if (int.TryParse(numericText, out int duration))
            {
                if (duration > 10000)
                {
                    duration = 10000;
                }

                NextUpDurationTextBox.Text = duration.ToString();
                SettingsManager.Current.NextUpDuration = duration;
            }
            else
            {
                NextUpDurationTextBox.Text = "2000";
                SettingsManager.Current.NextUpDuration = 2000;
            }
        }

        NextUpDurationTextBox.CaretIndex = NextUpDurationTextBox.Text.Length;
    }

    private void PlayerInfoSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.PlayerInfoEnabled = PlayerInfoSwitch.IsChecked ?? false;
    }
    
    private void SeekbarSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.SeekbarEnabled = SeekbarSwitch.IsChecked ?? false;
    }

    private void nIconLeftClickComboBox_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SettingsManager.Current.nIconLeftClick = nIconLeftClickComboBox.SelectedIndex;
    }

    private void CenterTitleArtistSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.CenterTitleArtist = CenterTitleArtistSwitch.IsChecked ?? false;
    }

    private void AnimationEasingStylesComboBox_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SettingsManager.Current.FlyoutAnimationEasingStyle = AnimationEasingStylesComboBox.SelectedIndex;
    }

    private void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            const string appName = "FluentFlyout";
            var executablePath = Environment.ProcessPath;

            if (enable)
            {
                // Check if the path is valid before setting
                if (File.Exists(executablePath))
                {
                    key.SetValue(appName, executablePath);
                }
                else
                {
                    throw new FileNotFoundException("Application executable not found");
                }
            }
            else
            {
                if (key.GetValue(appName) != null)
                {
                    key.DeleteValue(appName, false);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set startup: {ex.Message}");
        }
    }

    private void StartupHyperlink_RequestNavigate(object sender,
        System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void LockKeysSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.LockKeysEnabled = LockKeysSwitch.IsChecked ?? false;
    }

    private void LockKeysDurationTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string text = LockKeysDurationTextBox.Text.Trim();
        string numericText = new string(text.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(numericText))
        {
            LockKeysDurationTextBox.Text = "0";
            SettingsManager.Current.LockKeysDuration = 0;
        }
        else
        {
            LockKeysDurationTextBox.Text = numericText;
            if (int.TryParse(numericText, out int duration))
            {
                if (duration > 10000)
                {
                    duration = 10000;
                }

                LockKeysDurationTextBox.Text = duration.ToString();
                SettingsManager.Current.LockKeysDuration = duration;
            }
            else
            {
                LockKeysDurationTextBox.Text = "2000";
                SettingsManager.Current.LockKeysDuration = 2000;
            }
        }

        LockKeysDurationTextBox.CaretIndex = LockKeysDurationTextBox.Text.Length;
    }

    /// <summary>
    /// Changes the application theme when the selection is changed. 0 for default, 1 for light, 2 for dark.
    /// </summary>
    private void AppThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ThemeManager.ApplyAndSaveTheme(AppThemeComboBox.SelectedIndex);
    }

    private void MediaFlyoutEnabledSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.MediaFlyoutEnabled = MediaFlyoutEnabledSwitch.IsChecked ?? false;
    }

    private void nIconSymbolSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.nIconSymbol = nIconSymbolSwitch.IsChecked ?? false;
        ThemeManager.UpdateTrayIcon();
    }

    private void DisableIfFullscreenSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.DisableIfFullscreen = DisableIfFullscreenSwitch.IsChecked ?? false;
    }

    private void LockKeysBoldUISwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.LockKeysBoldUI = LockKeysBoldUISwitch.IsChecked ?? false;
    }

    private void PauseOtherSessionsEnabledSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.PauseOtherSessionsEnabled = PauseOtherSessionsEnabledSwitch.IsChecked ?? false;
    }
    
    private void LockKeysEnableInsertSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.LockKeysInsertEnabled = LockKeysEnableInsertSwitch.IsChecked ?? false;
    }

    private void BackgroundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SettingsManager.Current.MediaFlyoutBackgroundBlur = BackgroundComboBox.SelectedIndex;
    }

    private void AcrylicWindowSwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled = AcrylicWindowSwitch.IsChecked ?? false;
    }

    private void AppLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SettingsManager.Current.AppLanguage = ((ComboBoxItem)AppLanguageComboBox.SelectedItem).Tag.ToString() ?? "system";
        LocalizationManager.ApplyLocalization();
    }

    private void MediaFlyoutAlwaysDisplaySwitch_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.MediaFlyoutAlwaysDisplay = MediaFlyoutAlwaysDisplaySwitch.IsChecked ?? false;
        DurationTextBox.IsEnabled = !(MediaFlyoutAlwaysDisplaySwitch.IsChecked ?? false);
    }
}
