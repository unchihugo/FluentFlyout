using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FluentFlyout.Controls;

/// <summary>
/// Interaction logic for TaskbarVisualizerControl.xaml
/// </summary>
public partial class TaskbarVisualizerControl : UserControl
{
    // reference to main window for flyout functions
    private FluentFlyoutWPF.MainWindow? _mainWindow;
    private static readonly Visualizer visualizer = new();

    public TaskbarVisualizerControl()
    {
        InitializeComponent();

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

        if (SettingsManager.Current.TaskbarVisualizerEnabled)
        {
            visualizer.Start();
        }

        VisualizerContainer.Source = visualizer.Bitmap;
    }

    public void SetMainWindow(FluentFlyoutWPF.MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public static void OnTaskbarVisualizerEnabledChanged(bool value)
    {
        if (visualizer == null)
            return;

        if (value)
        {
            visualizer.Start();
        }
        else
        {
            visualizer.Stop();
        }
    }

    public static void DisposeVisualizer()
    {
        if (visualizer == null)
            return;

        visualizer.Dispose();
    }
}