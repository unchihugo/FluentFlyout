using FluentFlyout.Properties;
using MicaWPF.Controls;
using System.Windows;
using System.Windows.Documents;


namespace FluentFlyout
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : MicaWindow
    {
        public SettingsWindow()
        {
            InitializeComponent();

            PositionComboBox.SelectedIndex = Settings.Default.Position;
            StartupSwitch.IsChecked = Settings.Default.Startup;
            DurationTextBox.Document.Blocks.Clear();
            DurationTextBox.Document.Blocks.Add(new Paragraph(new Run(Settings.Default.Duration.ToString()))); // using rich text box because it looks nicer with MicaWPF
        }
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.Startup = StartupSwitch.IsChecked ?? true;
            Settings.Default.Save();
            DialogResult = true;
        }

        private void StartupSwitch_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (StartupSwitch.IsEnabled)
            {
                Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                key.SetValue("FluentFlyout", System.Windows.Forms.Application.ExecutablePath);
            }
            else
            {
                Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                key.DeleteValue("FluentFlyout", false);
            }
        }

        private void PositionComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            Settings.Default.Position = PositionComboBox.SelectedIndex;
            Settings.Default.Save();
        }

        private void RichTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var richTextBox = sender as System.Windows.Controls.RichTextBox;
            var textRange = new System.Windows.Documents.TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd);
            string text = textRange.Text.Trim();

            if (int.TryParse(text, out int duration))
            {
                Settings.Default.Duration = duration;
                Settings.Default.Save();
            }
        }
    }
}
