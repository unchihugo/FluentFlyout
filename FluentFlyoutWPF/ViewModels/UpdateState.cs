using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentFlyoutWPF.ViewModels;

/// <summary>
/// Runtime state for update checking (not persisted)
/// </summary>
public partial class UpdateState : ObservableObject
{
    public static UpdateState Current { get; } = new();

    /// <summary>
    /// Whether an update is available
    /// </summary>
    [ObservableProperty]
    public partial bool IsUpdateAvailable { get; set; }

    /// <summary>
    /// The newest version available from the API
    /// </summary>
    [ObservableProperty]
    public partial string NewestVersion { get; set; } = string.Empty;

    /// <summary>
    /// The URL to download the update (if available)
    /// </summary>
    [ObservableProperty]
    public partial string UpdateUrl { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the last update check
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastCheckedText))]
    public partial DateTime LastUpdateCheck { get; set; }

    /// <summary>
    /// Formatted text for the last update check time
    /// </summary>
    public string LastCheckedText => LastUpdateCheck == default 
        ? string.Empty 
        : LastUpdateCheck.ToString("G");
}