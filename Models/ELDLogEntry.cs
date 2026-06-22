using System;
using DutyStatus = OverWatchELD.Models.DutyStatus;

namespace OverWatchELD.UI.Models
{
    public class ELDLogEntry
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public DutyStatus Status { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}
