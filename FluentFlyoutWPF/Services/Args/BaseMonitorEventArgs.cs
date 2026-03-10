namespace FluentFlyoutWPF.Services.Args;
public class BaseMonitorEventArgs: EventArgs
{
    /// <summary>The source that triggered this event.</summary>
    public InputMonitorTrigger Trigger { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="BaseMonitorEventArgs"/>.
    /// </summary>
    /// <param name="trigger">The source that triggered this event.</param>
    public BaseMonitorEventArgs(InputMonitorTrigger trigger)
    {
        Trigger = trigger;
    }
}