using FluentFlyout.Properties;
using MicaWPF.Controls;
using MicaWPF.Core.Extensions;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Windows.ApplicationModel;


namespace FluentFlyout
{
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
            LayoutSwitch.IsChecked = Settings.Default.CompactLayout;
            PositionComboBox.SelectedIndex = Settings.Default.Position;
            FlyoutAnimationSpeedComboBox.SelectedIndex = Settings.Default.FlyoutAnimationSpeed;
            PlayerInfoSwitch.IsChecked = Settings.Default.PlayerInfoEnabled;
            RepeatSwitch.IsChecked = Settings.Default.RepeatEnabled;
            ShuffleSwitch.IsChecked = Settings.Default.ShuffleEnabled;
            StartupSwitch.IsChecked = Settings.Default.Startup;
            DurationTextBox.Text = Settings.Default.Duration.ToString();
            NextUpSwitch.IsChecked = Settings.Default.NextUpEnabled;
            NextUpDurationTextBox.Text = Settings.Default.NextUpDuration.ToString();
            nIconLeftClickComboBox.SelectedIndex = Settings.Default.nIconLeftClick;
            CenterTitleArtistSwitch.IsChecked = Settings.Default.CenterTitleArtist;

            try // gets the version of the app, works only in release mode
            {
                var version = Package.Current.Id.Version;
                VersionTextBlock.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                VersionTextBlock.Text = "debug version";
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LayoutSwitch_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.CompactLayout = LayoutSwitch.IsChecked ?? false;
            Settings.Default.Save();
        }

        private void StartupSwitch_Click(object sender, RoutedEventArgs e)
        {
            // might not work if installed using MSIX, needs investigation
            SetStartup(StartupSwitch.IsChecked ?? false);
            Settings.Default.Startup = StartupSwitch.IsChecked ?? false;
            Settings.Default.Save();
        }

        private void PositionComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            Settings.Default.Position = PositionComboBox.SelectedIndex;
            Settings.Default.Save();
        }

        private void RepeatSwitch_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.RepeatEnabled = RepeatSwitch.IsChecked ?? false;
            Settings.Default.Save();
        }

        private void ShuffleSwitch_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.ShuffleEnabled = ShuffleSwitch.IsChecked ?? false;
            Settings.Default.Save();
        }

        private void FlyoutAnimationSpeedComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            Settings.Default.FlyoutAnimationSpeed = FlyoutAnimationSpeedComboBox.SelectedIndex;
            Settings.Default.Save();
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

        private void NextUpSwitch_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.NextUpEnabled = NextUpSwitch.IsChecked ?? false;
            Settings.Default.Save();
        }

        private void DurationTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string text = DurationTextBox.Text.Trim();
            string numericText = new string(text.Where(char.IsDigit).ToArray());

            if (string.IsNullOrEmpty(numericText))
            {
                DurationTextBox.Text = "0";
                Settings.Default.Duration = 0;
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
                    Settings.Default.Duration = duration;
                }
                else
                {
                    DurationTextBox.Text = "3000";
                    Settings.Default.Duration = 3000;
                }
            }

            DurationTextBox.CaretIndex = DurationTextBox.Text.Length;
            Settings.Default.Save();
        }

        private void NextUpDurationTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string text = NextUpDurationTextBox.Text.Trim();
            string numericText = new string(text.Where(char.IsDigit).ToArray());

            if (string.IsNullOrEmpty(numericText))
            {
                NextUpDurationTextBox.Text = "0";
                Settings.Default.NextUpDuration = 0;
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
                    Settings.Default.NextUpDuration = duration;
                }
                else
                {
                    NextUpDurationTextBox.Text = "2000";
                    Settings.Default.NextUpDuration = 2000;
                }
            }

            NextUpDurationTextBox.CaretIndex = NextUpDurationTextBox.Text.Length;
            Settings.Default.Save();
        }

        private void PlayerInfoSwitch_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.PlayerInfoEnabled = PlayerInfoSwitch.IsChecked ?? false;
            Settings.Default.Save();
        }

        private void nIconLeftClickComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            Settings.Default.nIconLeftClick = nIconLeftClickComboBox.SelectedIndex;
            Settings.Default.Save();
        }

        private void CenterTitleArtistSwitch_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.CenterTitleArtist = CenterTitleArtistSwitch.IsChecked ?? false;
            Settings.Default.Save();
        }

        private void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key != null)
                    {
                        string appName = "FluentFlyout";
                        string executablePath = Environment.ProcessPath;

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
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to set startup: {ex.Message}");
            }
        }

        private void StartupHyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
