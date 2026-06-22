using System;

namespace OverWatchELD.Models.Vtc
{
    public sealed class DriverAwardDto
    {
        public string DriverId { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string AwardId { get; set; } = "";
        public DateTime AwardedUtc { get; set; }
        public string AwardedByUsername { get; set; } = "";
        public string Note { get; set; } = "";
        public VtcAwardDto? Award { get; set; }
    }
}