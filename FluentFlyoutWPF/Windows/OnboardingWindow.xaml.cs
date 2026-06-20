// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using MicaWPF.Controls;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace FluentFlyoutWPF;

public partial class OnboardingWindow : MicaWindow
{
    private static OnboardingWindow? instance;

    private readonly OnboardingViewModel _viewModel = new();
    private int _previousStepIndex = 0;
    private bool _isAnimating = false;

    public OnboardingWindow()
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

        DataContext = _viewModel;
        InitializeComponent();
        instance = this;

        Closed += (_, _) =>
        {
            instance = null;
            // open settings window when onboarding is closed
            OnOnboardingCompleted(Owner, EventArgs.Empty);
        };
        _viewModel.Completed += OnOnboardingCompleted;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Show loading ring for 0.5 seconds to prevent WPF-UI color flickers
        Loaded += async (_, _) =>
        {
            await Task.Delay(500);
            _viewModel.IsLoading = false;
        };

        MonitorUtil.UpdateMonitorList(
            FlyoutSelectedMonitorComboBox,
            () => SettingsManager.Current.FlyoutSelectedMonitor,
            value => SettingsManager.Current.FlyoutSelectedMonitor = value);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OnboardingViewModel.CurrentStepIndex) && !_isAnimating)
        {
            int targetStepIndex = _viewModel.CurrentStepIndex;
            bool isGoingForward = targetStepIndex > _previousStepIndex;

            // Start animation with old content still visible
            _isAnimating = true;
            _viewModel.IsTransitioning = true;
            AnimateContentTransition(isGoingForward, targetStepIndex);
        }
    }

    private void AnimateContentTransition(bool isGoingForward, int targetStepIndex)
    {
        var slideDistance = 30;
        var fadeOutDuration = TimeSpan.FromMilliseconds(150);
        var fadeInDuration = TimeSpan.FromMilliseconds(300);

        // Step 1: Revert to show old content during fade-out
        _viewModel.CurrentStepIndex = _previousStepIndex;

        // Step 2: Fade out and slide out old content
        var fadeOutAnimation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(fadeOutDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var slideOutAnimation = new DoubleAnimation
        {
            From = 0,
            To = isGoingForward ? -slideDistance : slideDistance,
            Duration = new Duration(fadeOutDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOutAnimation.Completed += (s, e) =>
        {
            // Step 3: After fade-out completes, switch to new content
            _viewModel.CurrentStepIndex = targetStepIndex;
            _previousStepIndex = targetStepIndex;

            // Step 4: Fade in and slide in new content
            var fadeInAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(fadeInDuration),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var slideInAnimation = new DoubleAnimation
            {
                From = isGoingForward ? slideDistance : -slideDistance,
                To = 0,
                Duration = fadeInDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeInAnimation.Completed += (s2, e2) =>
            {
                _isAnimating = false;
                _viewModel.IsTransitioning = false;
            };

            ContentsGrid.BeginAnimation(OpacityProperty, fadeInAnimation);
            ContentsGrid.RenderTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideInAnimation);
        };

        // Apply fade-out animations
        ContentsGrid.BeginAnimation(OpacityProperty, fadeOutAnimation);
        ContentsGrid.RenderTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideOutAnimation);
    }

    public static void ShowInstance()
    {
        if (instance == null)
        {
            new OnboardingWindow().Show();
            instance?.Activate();
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

    private void OnOnboardingCompleted(object? sender, EventArgs e)
    {
        SettingsWindow.ShowInstance();
        Close();
    }

    // same as in AboutPage.xaml.cs
    private async void UnlockPremiumButton_Click(object sender, RoutedEventArgs e)
    {
        FluentFlyout.Classes.LicenseManager.UnlockPremium(sender);
    }
}
