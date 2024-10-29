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
            DurationTextBox.Document.Blocks.Clear();
            DurationTextBox.Document.Blocks.Add(new Paragraph(new Run(Settings.Default.Duration.ToString()))); // using rich text box because it looks nicer with MicaWPF
            NextUpSwitch.IsChecked = Settings.Default.NextUpEnabled;
            NextUpDurationTextBox.Document.Blocks.Clear();
            NextUpDurationTextBox.Document.Blocks.Add(new Paragraph(new Run(Settings.Default.NextUpDuration.ToString())));

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

        private void RichTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var richTextBox = sender as System.Windows.Controls.RichTextBox;
            var textRange = new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd);
            string text = textRange.Text.Trim();

            if (int.TryParse(text, out int duration))
            {
                Settings.Default.Duration = duration;
                Settings.Default.Save();
            }
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

        private void NextUpDurationTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var richTextBox = sender as System.Windows.Controls.RichTextBox;
            var textRange = new TextRange(NextUpDurationTextBox.Document.ContentStart, NextUpDurationTextBox.Document.ContentEnd);
            string text = textRange.Text.Trim();

            if (int.TryParse(text, out int nextUpDuration))
            {
                Settings.Default.NextUpDuration = nextUpDuration;
                Settings.Default.Save();
            }
        }
    }
}
