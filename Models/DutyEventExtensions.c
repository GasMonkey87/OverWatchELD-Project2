using System;
using OverWatchELD.Models;

namespace OverWatchELD
{
    // Extension method so code like evt.EffectiveEndUtc() compiles,
    // even though EffectiveEndUtc is a property on DutyEvent.
    public static class DutyEventExtensions
    {
        public static DateTimeOffset EffectiveEndUtc(this DutyEvent e)
            = > e.EffectiveEndUtc;
    }
}
