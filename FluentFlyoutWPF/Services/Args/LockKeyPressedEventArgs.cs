namespace FluentFlyoutWPF.Services.Args;


/// <summary>
/// Data for a lock key event dispatched by <see cref="InputMonitorService"/>.
/// </summary>
public class LockKeyPressedEventArgs : BaseMonitorEventArgs
{

    /// <summary>The lock key that generated the event.</summary>
    public LockKeyType KeyType { get; }

    /// <summary>Current toggled state of the lock key after the key event.</summary>
    public bool IsToggled { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="LockKeyPressedEventArgs"/>.
    /// </summary>
    /// <param name="trigger">The source that triggered this lock key event.</param>
    /// <param name="keyType">The lock key that generated the event.</param>
    /// <param name="isToggled">Current toggled state of the lock key after the key event.</param>
    public LockKeyPressedEventArgs(InputMonitorTrigger trigger, LockKeyType keyType, bool isToggled): base(trigger)
    {
        KeyType = keyType;
        IsToggled = isToggled;
    }
}