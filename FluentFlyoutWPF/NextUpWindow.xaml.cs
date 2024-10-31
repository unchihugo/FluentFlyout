using FluentFlyout.Properties;
using FluentFlyoutWPF;
using MicaWPF.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Wpf.Ui.Controls;

namespace FluentFlyout
{
    /// <summary>
    /// Interaction logic for NextUpWindow.xaml
    /// </summary>
    public partial class NextUpWindow : MicaWindow
    {
        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        public NextUpWindow(string title, string artist, BitmapImage thumbnail)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -Width - 20;
            InitializeComponent();

            var titleWidth = GetStringWidth(title);
            var artistWidth = GetStringWidth(artist);

            if (titleWidth > artistWidth) Width = titleWidth + 142;
            else Width = artistWidth + 142;
            SongTitle.Text = title;
            SongArtist.Text = artist;
            SongImage.Source = thumbnail;
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
}
