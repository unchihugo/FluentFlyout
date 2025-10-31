﻿namespace FluentFlyout.Classes.Settings;

public class UserSettings
{
    public bool CompactLayout { get; set; }
    public int Position { get; set; }
    public int FlyoutAnimationSpeed { get; set; }
    public bool PlayerInfoEnabled { get; set; }
    public bool RepeatEnabled { get; set; }
    public bool ShuffleEnabled { get; set; }
    public bool Startup { get; set; }
    public int Duration { get; set; }
    public bool NextUpEnabled { get; set; }
    public int NextUpDuration { get; set; }
    public int nIconLeftClick { get; set; }
    public bool CenterTitleArtist { get; set; }
    public int FlyoutAnimationEasingStyle { get; set; }
    public bool LockKeysEnabled { get; set; }
    public int LockKeysDuration { get; set; }
    public int AppTheme { get; set; }
    public bool MediaFlyoutEnabled { get; set; }
    public bool MediaFlyoutAlwaysDisplay { get; set; }
    public bool nIconSymbol { get; set; }
    public bool DisableIfFullscreen { get; set; }
    public bool LockKeysBoldUI { get; set; }
    public string LastKnownVersion { get; set; } // for determining if user had updated to a new version
    public bool SeekbarEnabled { get; set; }
    public bool PauseOtherSessionsEnabled { get; set; } // pause other sessions when the user focuses on a new one
    public bool LockKeysInsertEnabled { get; set; } // whether pressing insert key should display the LockKeys flyout
    public int MediaFlyoutBackgroundBlur { get; set; } // media flyout presets for background blur styles
    public bool MediaFlyoutAcrylicWindowEnabled { get; set; } // enable acrylic blur effect on the flyout window (deprecated - kept for backward compatibility)
    public bool MediaAcrylicWindowEnabled { get; set; } // enable acrylic blur effect on the media flyout window
    public bool NextUpAcrylicWindowEnabled { get; set; } // enable acrylic blur effect on the Next Up window
    public bool LockKeysAcrylicWindowEnabled { get; set; } // enable acrylic blur effect on the Lock Keys window
    public string AppLanguage { get; set; } // user's preferred app language

    // default user settings for new users, existing user settings take from here when new settings appear
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
        nIconLeftClick = 0;
        CenterTitleArtist = false;
        FlyoutAnimationEasingStyle = 2;
        LockKeysEnabled = true;
        LockKeysDuration = 2000;
        AppTheme = 0;
        MediaFlyoutEnabled = true;
        MediaFlyoutAlwaysDisplay = false;
        nIconSymbol = false;
        DisableIfFullscreen = true;
        LockKeysBoldUI = true;
        LastKnownVersion = "";
        SeekbarEnabled = false;
        PauseOtherSessionsEnabled = false;
        LockKeysInsertEnabled = true;
        MediaFlyoutBackgroundBlur = 0;
        MediaFlyoutAcrylicWindowEnabled = true;
        MediaAcrylicWindowEnabled = true;
        NextUpAcrylicWindowEnabled = true;
        LockKeysAcrylicWindowEnabled = true;
        AppLanguage = "system";
    }
}
