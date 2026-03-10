namespace FluentFlyoutWPF.Services.Args;

/// <summary>
/// Data for a volume change event dispatched by <see cref="InputMonitorService"/>.
/// </summary>
public class VolumeChangedEventArgs : BaseMonitorEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VolumeChangedEventArgs"/> class.
    /// </summary>
    /// <param name="trigger">The monitor source that triggered this volume change event.</param>
    public VolumeChangedEventArgs(InputMonitorTrigger trigger) : base(trigger)
    {
    }
}