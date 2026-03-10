namespace FluentFlyoutWPF.Services.Args;

/// <summary>
/// Data for a media key pressed event dispatched by <see cref="InputMonitorService"/>.
/// </summary>
public class MediaKeyPressedEventArgs : BaseMonitorEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MediaKeyPressedEventArgs"/> class.
    /// </summary>
    /// <param name="trigger">The monitor source that triggered this media key event.</param>
    public MediaKeyPressedEventArgs(InputMonitorTrigger trigger) : base(trigger)
    {
    }
}