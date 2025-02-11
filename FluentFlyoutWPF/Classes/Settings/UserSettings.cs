namespace FluentFlyout.Classes.Settings;

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
    public bool nIconSymbol { get; set; }
    public bool DisableIfFullscreen { get; set; }
    public bool LockKeysBoldUI { get; set; }
    public string LastKnownVersion { get; set; }
    public bool SeekbarEnabled { get; set; }


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
        nIconSymbol = false;
        DisableIfFullscreen = true;
        LockKeysBoldUI = true;
        LastKnownVersion = "";
        SeekbarEnabled = true;
    }
}