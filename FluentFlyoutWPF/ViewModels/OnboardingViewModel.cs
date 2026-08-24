// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyout.Classes.Settings;
using System.Windows;

namespace FluentFlyoutWPF.ViewModels;

public sealed class OnboardingStep
{
    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string ImageSource { get; init; }
}

public class OnboardingViewModel : ObservableObject
{
    private int _currentStepIndex;
    private bool _isLoading = true;
    private bool _isTransitioning;

    public UserSettings Settings => SettingsManager.Current;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsTransitioning
    {
        get => _isTransitioning;
        set
        {
            if (SetProperty(ref _isTransitioning, value))
            {
                GoBackCommand.NotifyCanExecuteChanged();
                GoNextCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<OnboardingStep> Steps { get; } =
    [
        new OnboardingStep
        {
            Title = Application.Current.TryFindResource("MediaFlyoutTitle").ToString(),
            Description = Application.Current.TryFindResource("MediaFlyoutDescription").ToString(),
            ImageSource = "/Resources/Onboarding/MediaFlyout.png"
        },
        new OnboardingStep
        {
            Title = Application.Current.TryFindResource("VolumeFlyoutTitle").ToString(),
            Description = Application.Current.TryFindResource("VolumeFlyoutDescription").ToString(),
            ImageSource = "/Resources/FluentFlyoutVolumeDemo.png"
        },
        new OnboardingStep
        {
            Title = Application.Current.TryFindResource("LockKeysCustomizationTitle").ToString(),
            Description = Application.Current.TryFindResource("LockKeysDescription").ToString(),
            ImageSource = "/Resources/Onboarding/LockKeysFlyout.png"
        },
        new OnboardingStep
        {
            Title = Application.Current.TryFindResource("UnlockFullExperienceText").ToString(),
            Description = "",
            ImageSource = "/Resources/Onboarding/Taskbar.png"
        }
    ];

    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        set
        {
            if (SetProperty(ref _currentStepIndex, value))
            {
                OnPropertyChanged(nameof(CurrentStep));
                OnPropertyChanged(nameof(StepProgressText));
                OnPropertyChanged(nameof(NextButtonText));
                OnPropertyChanged(nameof(IsLastStep));
                OnPropertyChanged(nameof(IsMediaStep));
                OnPropertyChanged(nameof(IsVolumeStep));
                OnPropertyChanged(nameof(IsLockKeysStep));
                OnPropertyChanged(nameof(IsPremiumStep));
                OnPropertyChanged(nameof(CanGoBack));
                GoBackCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public OnboardingStep CurrentStep => Steps[CurrentStepIndex];

    public string StepProgressText => string.Format(Application.Current.TryFindResource("OnboardingStepsCounter").ToString(), CurrentStepIndex + 1, Steps.Count);

    public string? NextButtonText => IsLastStep ? Application.Current.TryFindResource("Finish").ToString() : Application.Current.TryFindResource("Next").ToString();

    public bool IsLastStep => CurrentStepIndex >= Steps.Count - 1;

    public bool IsMediaStep => CurrentStepIndex == 0;

    public bool IsVolumeStep => CurrentStepIndex == 1;

    public bool IsLockKeysStep => CurrentStepIndex == 2;

    public bool IsPremiumStep => CurrentStepIndex == 3;

    public bool CanGoBack => CurrentStepIndex > 0;

    public event EventHandler? Completed;

    public IRelayCommand GoBackCommand { get; }

    public IRelayCommand GoNextCommand { get; }

    public IRelayCommand SkipCommand { get; }

    public OnboardingViewModel()
    {
        GoBackCommand = new RelayCommand(GoBack, () => CanGoBack && !IsTransitioning);
        GoNextCommand = new RelayCommand(GoNext, () => !IsTransitioning);
        SkipCommand = new RelayCommand(Skip);
    }

    private void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        CurrentStepIndex--;
    }

    private void GoNext()
    {
        if (IsLastStep)
        {
            SettingsManager.SaveSettings();
            Completed?.Invoke(this, EventArgs.Empty);
            return;
        }

        CurrentStepIndex++;
    }

    private void Skip()
    {
        SettingsManager.SaveSettings();
        Completed?.Invoke(this, EventArgs.Empty);
    }
}