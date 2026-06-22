namespace ATS_ELD.ViewModels;

/// <summary>
/// Simple availability indicator used by the Dispatch/Messaging tab.
/// Kept in the ViewModels namespace so CommunityToolkit source-generated code
/// (which references DriverAvailability without a namespace qualifier) resolves cleanly.
/// </summary>
public enum DriverAvailability
{
    Offline = 0,
    Available = 1,
    Busy = 2,
    Driving = 3,
    OnDuty = 4,
    Sleeper = 5
}
