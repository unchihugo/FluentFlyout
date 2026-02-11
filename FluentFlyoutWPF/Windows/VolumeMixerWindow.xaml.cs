// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.ViewModels;
using MicaWPF.Controls;
using System.Windows;

namespace FluentFlyoutWPF.Windows;

/// <summary>
/// Interaction logic for VolumeMixerWindow.xaml
/// </summary>
public partial class VolumeMixerWindow : MicaWindow
{
    public VolumeMixerViewModel ViewModel { get; } = new();
    public UserSettings UserSettings => SettingsManager.Current;

    private MainWindow _mainWindow;

    public VolumeMixerWindow()
    {
        DataContext = this;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        CustomWindowChrome.CaptionHeight = 0;
        CustomWindowChrome.UseAeroCaptionButtons = false;
        CustomWindowChrome.GlassFrameThickness = new Thickness(0);

        _mainWindow = (MainWindow)Application.Current.MainWindow;
        Show();
    }

    private void MicaWindow_Loaded(object sender, RoutedEventArgs e)
    {
        WindowHelper.SetTopmost(this);
        _mainWindow.OpenAnimation(this, true);
    }
}