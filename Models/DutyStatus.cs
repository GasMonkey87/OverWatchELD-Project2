namespace OverWatchELD.Models
{
    public enum DutyStatus
    {
        Unknown = 0,

        OffDuty = 1,
        Off = OffDuty, // alias for older code

        Sleeper = 2,
        SleeperBerth = Sleeper, // alias for older code

        Driving = 3,
        OnDuty = 4,

        PersonalConveyance = 5,
        YardMove = 6
    }
}
