using System.Collections.ObjectModel;
using System.Xml.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentFlyoutWPF.Models;

namespace FluentFlyout.Classes.Settings;

/**
 * User Settings data model.
 */
public partial class UserSettings : ObservableObject
{
    /// <summary>
    /// Use a compact layout
    /// </summary>
    [ObservableProperty] public partial bool CompactLayout { get; set; }

    /// <summary>
    /// Flyout position on screen
    /// </summary>
    [ObservableProperty] public partial int Position { get; set; }

    /// <summary>
    /// Scale for flyout animation speed
    /// </summary>
    [ObservableProperty] public partial int FlyoutAnimationSpeed { get; set; }

    /// <summary>
    /// Show player information in the flyout
    /// </summary>
    [ObservableProperty] public partial bool PlayerInfoEnabled { get; set; }

    /// <summary>
    /// Enable repeat button
    /// </summary>
    [ObservableProperty] public partial bool RepeatEnabled { get; set; }

    /// <summary>
    /// Enable shuffle button
    /// </summary>
    [ObservableProperty] public partial bool ShuffleEnabled { get; set; }

    /// <summary>
    /// Start minimized to tray when Windows starts
    /// </summary>
    [ObservableProperty] public partial bool Startup { get; set; }

    /// <summary>
    /// MediaFlyout Always Display
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDurationEditable))] 
    public partial bool MediaFlyoutAlwaysDisplay { get; set; }
    
    [XmlIgnore]
    public bool IsDurationEditable => !MediaFlyoutAlwaysDisplay;
    
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
            OnPropertyChanged();
        }
    }
    
    /// <summary>
    /// Enable the 'Next Up' flyout (experimental)
    /// </summary>
    [ObservableProperty] public partial bool NextUpEnabled { get; set; }

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

            OnPropertyChanged();
        }
    }
    /// <summary>
    /// Tray icon left-click behavior
    /// </summary>
    [ObservableProperty] public partial int NIconLeftClick { get; set; }

    /// <summary>
    /// Center the title and artist text
    /// </summary>
    [ObservableProperty] public partial bool CenterTitleArtist { get; set; }

    /// <summary>
    /// Animation easing style index
    /// </summary>
    [ObservableProperty] public partial int FlyoutAnimationEasingStyle { get; set; }

    /// <summary>
    /// Enable lock keys flyout (shows Caps/Num/Scroll status)
    /// </summary>
    [ObservableProperty] public partial bool LockKeysEnabled { get; set; }

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

            OnPropertyChanged();
        }
    }
    /// <summary>
    /// App theme. 0 for default, 1 for light, 2 for dark.
    /// </summary>
    [ObservableProperty] public partial int AppTheme { get; set; }

    /// <summary>
    /// Enable media flyout
    /// </summary>
    [ObservableProperty] public partial bool MediaFlyoutEnabled { get; set; }

    /// <summary>
    /// Use symbol-style tray icon
    /// </summary>
    [ObservableProperty] public partial bool NIconSymbol { get; set; }

    /// <summary>
    /// Disable flyout when a DirectX exclusive fullscreen app is detected
    /// </summary>
    [ObservableProperty] public partial bool DisableIfFullscreen { get; set; }

    /// <summary>
    /// Use bold symbol and font in the lock keys flyout
    /// </summary>
    [ObservableProperty] public partial bool LockKeysBoldUi { get; set; }

    /// <summary>
    /// Determines if the user has updated to a new version
    /// </summary>
    [ObservableProperty]
    public partial string LastKnownVersion { get; set; }

    /// <summary>
    /// Show seekbar if the player supports it
    /// </summary>
    [ObservableProperty] public partial bool SeekbarEnabled { get; set; }

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
    /// User's preferred app language (e.g., "system" for system default)
    /// </summary>
    [ObservableProperty]
    public partial string AppLanguage { get; set; }
    
    /// <summary>
    /// Language Options
    /// </summary>
    [XmlIgnore]
    public ObservableCollection<LanguageOption> LanguageOptions { get; } =
    [
        new("System", "system"),
        new("English", "en-US"),
        new("Nederlands", "nl-NL"),
        new("Tiếng Việt", "vi-VN")
    ];

    
    [XmlIgnore]
    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    public UserSettings()
    {
        CompactLayout = false;
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
        NIconSymbol = false;
        DisableIfFullscreen = true;
        LockKeysBoldUi = true;
        LastKnownVersion = "";
        SeekbarEnabled = false;
        PauseOtherSessionsEnabled = false;
        LockKeysInsertEnabled = true;
        MediaFlyoutBackgroundBlur = 0;
        MediaFlyoutAcrylicWindowEnabled = true;
        AppLanguage = "system";
        _initializing = false;
    }
    
    
    private static bool _initializing = true;
    
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
}