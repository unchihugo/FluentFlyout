using FluentFlyout.Properties;
using MicaWPF.Controls;
using Microsoft.Win32;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using Windows.ApplicationModel;


namespace FluentFlyout
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : MicaWindow
    {
        private static SettingsWindow? instance;

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

            LayoutSwitch.IsChecked = Settings.Default.CompactLayout;
            PositionComboBox.SelectedIndex = Settings.Default.Position;
            FlyoutAnimationSpeedComboBox.SelectedIndex = Settings.Default.FlyoutAnimationSpeed;
            RepeatSwitch.IsChecked = Settings.Default.RepeatEnabled;
            ShuffleSwitch.IsChecked = Settings.Default.ShuffleEnabled;
            StartupSwitch.IsChecked = Settings.Default.Startup;
            DurationTextBox.Text = Settings.Default.Duration.ToString();
            NextUpSwitch.IsChecked = Settings.Default.NextUpEnabled;
            NextUpDurationTextBox.Text = Settings.Default.NextUpDuration.ToString();

            try
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
            if (StartupSwitch.IsChecked == true)
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                string executablePath = Assembly.GetExecutingAssembly().Location;
                key.SetValue("FluentFlyout", executablePath);
            }
            else
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                string executablePath = Assembly.GetExecutingAssembly().Location;
                key.DeleteValue("FluentFlyout");
            }
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
    }
}
