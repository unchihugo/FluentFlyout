// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Settings;
using FluentFlyoutWPF.Classes.Utils;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FluentFlyoutWPF.Controls;

/// <summary>
/// Interaction logic for TaskbarVisualizerControl.xaml
/// </summary>
public partial class TaskbarVisualizerControl : UserControl
{
    private const double DefaultTaskbarVisualizerHeight = 40;
    private const double SmallTaskbarVisualizerHeight = 28;

    // reference to main window for flyout functions
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

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
    }

    public void SetSmallTaskbarMode(bool isSmallTaskbar)
    {
        Height = isSmallTaskbar ? SmallTaskbarVisualizerHeight : DefaultTaskbarVisualizerHeight;
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

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

        TaskbarHoverEffect.Apply(MainBorder, TopBorder);
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

        TaskbarHoverEffect.Clear(MainBorder, TopBorder);
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // only continue when the visualizer is clickable and actually has content
        // otherwise it would show an empty container to click on which is weird
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

        // open settings when clicked
        SettingsWindow.ShowInstance("TaskbarVisualizerPage");
    }
}