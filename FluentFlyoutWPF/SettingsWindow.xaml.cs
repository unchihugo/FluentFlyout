using FluentFlyout.Properties;
using MicaWPF.Controls;
using System.Windows;


namespace FluentFlyout
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : MicaWindow
    {
        public int Position = 0;

        public SettingsWindow(int currentPosition)
        {
            InitializeComponent();

            PositionComboBox.SelectedIndex = currentPosition;
            StartupSwitch.IsChecked = Settings.Default.Startup;
        }
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Position = PositionComboBox.SelectedIndex;
            Settings.Default.Position = Position;
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
    }
}
