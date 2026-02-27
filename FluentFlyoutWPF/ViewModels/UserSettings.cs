// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Controls;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Windows;
using System.Collections.ObjectModel;
using System.Windows;
using System.Xml.Serialization;

namespace FluentFlyoutWPF.ViewModels;

/**
 * User Settings data model.
 */
public partial class UserSettings : ObservableObject
{
    /// <summary>
    /// Use a compact layout
    /// </summary>
    [ObservableProperty]
    public partial bool CompactLayout { get; set; }

    /// <summary>
    /// Flyout Target Display
    /// </summary>
    [ObservableProperty]
    public partial int FlyoutSelectedMonitor { get; set; }

    /// <summary>
    /// Flyout position on screen
    /// </summary>
    [ObservableProperty]
    public partial int Position { get; set; }

    /// <summary>
    /// Scale for flyout animation speed
    /// </summary>
    [ObservableProperty]
    public partial int FlyoutAnimationSpeed { get; set; }

    /// <summary>
    /// Show player information in the flyout
    /// </summary>
    [ObservableProperty]
    public partial bool PlayerInfoEnabled { get; set; }

    /// <summary>
    /// Enable repeat button
    /// </summary>
    [ObservableProperty]
    public partial bool RepeatEnabled { get; set; }

    /// <summary>
    /// Enable shuffle button
    /// </summary>
    [ObservableProperty]
    public partial bool ShuffleEnabled { get; set; }

    /// <summary>
    /// Start minimized to tray when Windows starts
    /// </summary>
    [ObservableProperty]
    public partial bool Startup { get; set; }

    /// <summary>
    /// MediaFlyout Always Display
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDurationEditable))]
    public partial bool MediaFlyoutAlwaysDisplay { get; set; }

    [XmlIgnore] public bool IsDurationEditable => !MediaFlyoutAlwaysDisplay;

    /// <summary>
    /// Flyout display duration (milliseconds)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    public partial int Duration { get; set; }

    [XmlIgnore]
    public string DurationText
    {
        get => Duration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                Duration = result switch
                {
                    > 10000 => 10000,
                    < 0 => 0,
                    _ => result
                };
            }
            else
            {
                Duration = 3000;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Enable the 'Next Up' flyout (experimental)
    /// </summary>
    [ObservableProperty]
    public partial bool NextUpEnabled { get; set; }

    /// <summary>
    /// 'Next Up' flyout display duration (milliseconds)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextUpDurationText))]
    public partial int NextUpDuration { get; set; }

    [XmlIgnore]
    public string NextUpDurationText
    {
        get => NextUpDuration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                NextUpDuration = result switch
                {
                    > 10000 => 10000,
                    < 0 => 0,
                    _ => result
                };
            }
            else
            {
                NextUpDuration = 2000;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Tray icon left-click behavior
    /// </summary>
    [ObservableProperty]
    [XmlElement(ElementName = "nIconLeftClick")]
    public partial int NIconLeftClick { get; set; }

    /// <summary>
    /// Center the title and artist text
    /// </summary>
    [ObservableProperty]
    public partial bool CenterTitleArtist { get; set; }

    /// <summary>
    /// Animation easing style index
    /// </summary>
    [ObservableProperty]
    public partial int FlyoutAnimationEasingStyle { get; set; }

    /// <summary>
    /// Enable lock keys flyout (shows Caps/Num/Scroll status)
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysEnabled { get; set; }

    /// <summary>
    /// Lock keys flyout display duration (milliseconds)
    /// </summary>

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockKeysDurationText))]
    public partial int LockKeysDuration { get; set; }

    [XmlIgnore]
    public string LockKeysDurationText
    {
        get => LockKeysDuration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                LockKeysDuration = result switch
                {
                    > 10000 => 10000,
                    < 0 => 0,
                    _ => result
                };
            }
            else
            {
                LockKeysDuration = 2000;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// App theme. 0 for default, 1 for light, 2 for dark.
    /// </summary>
    [ObservableProperty]
    public partial int AppTheme { get; set; }

    /// <summary>
    /// Enable media flyout
    /// </summary>
    [ObservableProperty]
    public partial bool MediaFlyoutEnabled { get; set; }

    /// <summary>
    /// Exclude volume keys from triggering media flyout
    /// </summary>
    [ObservableProperty]
    public partial bool MediaFlyoutVolumeKeysExcluded { get; set; }

    /// <summary>
    /// Use symbol-style tray icon
    /// </summary>
    [ObservableProperty]
    [XmlElement(ElementName = "nIconSymbol")]
    public partial bool NIconSymbol { get; set; }

    /// <summary>
    /// Hide tray icon completely
    /// </summary>
    [ObservableProperty]
    public partial bool NIconHide { get; set; }

    /// <summary>
    /// Disable flyout when a DirectX exclusive fullscreen app is detected
    /// </summary>
    [ObservableProperty]
    public partial bool DisableIfFullscreen { get; set; }

    /// <summary>
    /// Use bold symbol and font in the lock keys flyout
    /// </summary>
    [ObservableProperty]
    [XmlElement(ElementName = "LockKeysBoldUI")]
    public partial bool LockKeysBoldUi { get; set; }

    /// <summary>
    /// Determines if the user has updated to a new version
    /// </summary>
    [ObservableProperty]
    public partial string LastKnownVersion { get; set; }

    /// <summary>
    /// Show seekbar if the player supports it
    /// </summary>
    [ObservableProperty]
    public partial bool SeekbarEnabled { get; set; }

    /// <summary>
    /// Pause other media sessions when focusing a new one
    /// </summary>
    [ObservableProperty]
    public partial bool PauseOtherSessionsEnabled { get; set; }

    /// <summary>
    /// Show LockKeys flyout when the Insert key is pressed
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysInsertEnabled { get; set; }

    /// <summary>
    /// Preset for media flyout background blur styles
    /// </summary>
    [ObservableProperty]
    public partial int MediaFlyoutBackgroundBlur { get; set; }

    /// <summary>
    /// Enable acrylic blur effect on the flyout window
    /// </summary>
    [ObservableProperty]
    public partial bool MediaFlyoutAcrylicWindowEnabled { get; set; }

    /// <summary>
    /// Enable acrylic blur effect on the Next Up window
    /// </summary>
    [ObservableProperty]
    public partial bool NextUpAcrylicWindowEnabled { get; set; }

    /// <summary>
    /// Enable acrylic blur effect on the Lock Keys window
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysAcrylicWindowEnabled { get; set; }

    [ObservableProperty]
    public partial bool VolumeMixerAcrylicWindowEnabled { get; set; }

    /// <summary>
    /// User's preferred app language (e.g., "system" for system default)
    /// </summary>
    [ObservableProperty]
    public partial string AppLanguage { get; set; }

    /// <summary>
    /// Language Options
    /// </summary>
    [XmlIgnore]
    public ObservableCollection<LanguageOption> LanguageOptions { get; } = [];

    [XmlIgnore]
    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial FlowDirection FlowDirection { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial string FontFamily { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget is enabled
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetEnabled { get; set; }

    /// <summary>
    /// Widget Target Display
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarWidgetSelectedMonitor { get; set; }

    /// <summary>
    /// Gets or sets the position of the taskbar widget, represented as an integer value.
    /// 0: Left, 1: Center, 2: Right
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarWidgetPosition { get; set; }

    /// <summary>
    /// Determines whether padding should be applied to the taskbar widget for the native Windows Widgets button
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetPadding { get; set; }

    /// <summary>
    /// Manual padding value in pixels applied to the taskbar widget
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TaskbarWidgetManualPaddingText))]
    public partial int TaskbarWidgetManualPadding { get; set; }

    [XmlIgnore]
    public string TaskbarWidgetManualPaddingText
    {
        get => TaskbarWidgetManualPadding.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                TaskbarWidgetManualPadding = result switch
                {
                    > 9999 => 9999,
                    < -9999 => -9999,
                    _ => result
                };
            }
            else
            {
                TaskbarWidgetManualPadding = 0;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget is clickable
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetClickable { get; set; }

    /// <summary>
    /// Gets or sets a value indication whether the taskbar widget background should have a blur effect
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetBackgroundBlur { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget should be completely hidden from view when no media is playing.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetHideCompletely { get; set; }

    /// <summary>
    /// Whether taskbar widget controls (pause, previous, next) are enabled.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetControlsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget should play animations.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetAnimated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar visualizer is enabled.
    /// </summary>
    /// <remarks>For now, this requires Premium and Taskbar Widget to be enabled.</remarks>
    [ObservableProperty]
    public partial bool TaskbarVisualizerEnabled { get; set; }

    /// <summary>
    /// Position of the visualizer, where 0 and 1 are to the left or right of the widget.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerPosition { get; set; }

    /// <summary>
    /// The number of visualizer bars to display.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerBarCount { get; set; }

    /// <summary>
    /// Whether the visualizer should be symmetrical/mirrored.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarVisualizerCenteredBars { get; set; }

    /// <summary>
    /// Gets or sets whether a bar baseline is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarVisualizerBaseline { get; set; }

    /// <summary>
    /// Gets or sets the audio sensitivity for the taskbar visualizer from 1 to 3, where 2 is the default.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerAudioSensitivity { get; set; }

    [ObservableProperty]
    public partial bool VolumeControlEnabled { get; set; }

    [ObservableProperty]
    public partial bool VolumeControlAboveMediaFlyout { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeControlDurationText))]
    public partial int VolumeControlDuration { get; set; }

    [XmlIgnore]
    public string VolumeControlDurationText
    {
        get => VolumeControlDuration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                VolumeControlDuration = result switch
                {
                    > 10000 => 10000,
                    < 0 => 0,
                    _ => result
                };
            }
            else
            {
                VolumeControlDuration = 3000;
            }

            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial bool VolumeMixerEnabled { get; set; }

    [ObservableProperty]
    public partial bool VolumeMixerHighlightActiveApps { get; set; }

    /// <summary>
    /// Gets whether premium features are unlocked (runtime only, not persisted)
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial bool IsPremiumUnlocked { get; set; }

    /// <summary>
    /// Gets or sets the opacity level of the acrylic blur effect.
    /// </summary>
    [ObservableProperty]
    public partial uint AcrylicBlurOpacity { get; set; }

    /// <summary>
    /// Gets whether this is a Store version. Once false, always false (only if last known version was not null).
    /// </summary>
    [ObservableProperty]
    public partial bool IsStoreVersion { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial string PremiumPrice { get; set; }

    /// <summary>
    /// Last time the program has sent an update notification in Unix seconds.
    /// </summary>
    [ObservableProperty]
    public partial long LastUpdateNotificationUnixSeconds { get; set; }

    /// <summary>
    /// Determines whether user will get Windows notifications when a new update is available.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowUpdateNotifications { get; set; }

    [XmlIgnore]
    private bool _initializing = true;

    public UserSettings()
    {
        foreach (var supportedLanguage in LocalizationManager.SupportedLanguages)
        {
            LanguageOptions.Add(new LanguageOption(supportedLanguage.Key, supportedLanguage.Value));
        }

        CompactLayout = false;
        FlyoutSelectedMonitor = 0;
        Position = 0;
        FlyoutAnimationSpeed = 2;
        PlayerInfoEnabled = true;
        RepeatEnabled = false;
        ShuffleEnabled = false;
        Startup = true;
        Duration = 3000;
        NextUpEnabled = false;
        NextUpDuration = 2000;
        NIconLeftClick = 0;
        CenterTitleArtist = false;
        FlyoutAnimationEasingStyle = 2;
        LockKeysEnabled = true;
        LockKeysDuration = 2000;
        AppTheme = 0;
        MediaFlyoutEnabled = true;
        MediaFlyoutAlwaysDisplay = false;
        MediaFlyoutVolumeKeysExcluded = false;
        NIconSymbol = false;
        NIconHide = false;
        DisableIfFullscreen = true;
        LockKeysBoldUi = false;
        LastKnownVersion = "";
        SeekbarEnabled = false;
        PauseOtherSessionsEnabled = false;
        LockKeysInsertEnabled = true;
        MediaFlyoutBackgroundBlur = 0;
        AppLanguage = "system";
        FlowDirection = FlowDirection.LeftToRight;
        FontFamily = "Segoe UI Variable, Microsoft YaHei UI, Yu Gothic UI";
        MediaFlyoutAcrylicWindowEnabled = true;
        NextUpAcrylicWindowEnabled = true;
        LockKeysAcrylicWindowEnabled = true;
        VolumeMixerAcrylicWindowEnabled = true;
        TaskbarWidgetEnabled = false;
        TaskbarWidgetSelectedMonitor = 0;
        TaskbarWidgetPosition = 0;
        TaskbarWidgetPadding = true;
        TaskbarWidgetManualPadding = 0;
        TaskbarWidgetClickable = true;
        TaskbarWidgetBackgroundBlur = false;
        TaskbarWidgetHideCompletely = false;
        TaskbarWidgetControlsEnabled = false;
        TaskbarWidgetAnimated = true;
        TaskbarVisualizerEnabled = false;
        TaskbarVisualizerPosition = 0;
        TaskbarVisualizerBarCount = 10;
        TaskbarVisualizerCenteredBars = false;
        TaskbarVisualizerBaseline = false;
        TaskbarVisualizerAudioSensitivity = 2;
        VolumeControlEnabled = false;
        VolumeControlAboveMediaFlyout = false;
        VolumeControlDuration = 3000;
        VolumeMixerEnabled = true;
        VolumeMixerHighlightActiveApps = true;
        AcrylicBlurOpacity = 175;
        LastUpdateNotificationUnixSeconds = 0;
        ShowUpdateNotifications = true;
    }

    /// <summary>
    /// Called after deserialization to finalize initialization
    /// </summary>
    internal void CompleteInitialization()
    {
        _initializing = false;
    }

    partial void OnAppLanguageChanged(string oldValue, string newValue)
    {
        if (oldValue == newValue) return;
        SelectedLanguage = LanguageOptions.First(l => l.Tag == newValue);
    }

    partial void OnSelectedLanguageChanged(LanguageOption oldValue, LanguageOption newValue)
    {
        if (oldValue == newValue || _initializing) return;
        AppLanguage = newValue.Tag;
        LocalizationManager.ApplyLocalization();
    }

    /// <summary>
    /// Changes the application theme when the selection is changed. 0 for default, 1 for light, 2 for dark.
    /// </summary>
    partial void OnAppThemeChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        ThemeManager.ApplyAndSaveTheme(newValue);
    }

    partial void OnNIconSymbolChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        ThemeManager.UpdateTrayIcon();
    }

    partial void OnAcrylicBlurOpacityChanged(uint oldValue, uint newValue)
    {
        if (oldValue == newValue || _initializing) return;
        WindowBlurHelper.AdjustBlurOpacityForAllWindows(newValue);
    }

    partial void OnTaskbarWidgetEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;

        // Check premium status before allowing widget to be enabled
        if (newValue && !SettingsManager.Current.IsPremiumUnlocked)
        {
            // Revert the change if premium is not unlocked
            TaskbarWidgetEnabled = false;
            return;
        }

        UpdateTaskbar();
    }

    // Update taskbar when relevant settings change
    partial void OnTaskbarWidgetPositionChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetManualPaddingChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetClickableChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetBackgroundBlurChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetHideCompletelyChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetControlsEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarVisualizerPositionChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    private void UpdateTaskbar()
    {
        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow.UpdateTaskbar();
    }

    partial void OnTaskbarVisualizerEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        TaskbarVisualizerControl.OnTaskbarVisualizerEnabledChanged(newValue);
        UpdateTaskbar();
    }

    partial void OnTaskbarVisualizerBarCountChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        Visualizer.ResizeBarList(newValue);
    }

    partial void OnVolumeControlEnabledChanged(bool oldValue, bool newValue)
    {
        if (newValue == true || oldValue == newValue || _initializing) return;

        // re-enable native volume flyout
        VolumeMixerWindow.ShowVolumeOsd();
    }
}