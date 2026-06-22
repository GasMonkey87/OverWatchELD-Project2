using System;

namespace OverWatchELD.Models
{
    public class FleetCostEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string TruckId { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string PlateNumber { get; set; } = "";

        public DateTime DateUtc { get; set; } = DateTime.UtcNow;

        public string BillType { get; set; } = "Fuel";
        public decimal Amount { get; set; }

        public double OdometerMiles { get; set; }

        public string Vendor { get; set; } = "";
        public string Location { get; set; } = "";
        public string Notes { get; set; } = "";

        public bool RequiresFollowUp { get; set; }
        public bool IsResolved { get; set; }

        public double? DueAtMiles { get; set; }
        public DateTime? DueDateUtc { get; set; }

        public string Status
        {
            get
            {
                if (IsResolved) return "Resolved";
                if (IsOverdueByMiles || IsOverdueByDate) return "Overdue";
                if (RequiresFollowUp) return "Follow-up";
                return "Open";
            }
        }

        public bool IsOverdueByMiles =>
            !IsResolved &&
            DueAtMiles.HasValue &&
            OdometerMiles >= DueAtMiles.Value;

        public bool IsOverdueByDate =>
            !IsResolved &&
            DueDateUtc.HasValue &&
            DueDateUtc.Value.Date < DateTime.UtcNow.Date;

        public string DateLocalText => DateUtc.ToLocalTime().ToString("g");
        public string DueDateLocalText => DueDateUtc?.ToLocalTime().ToString("d") ?? "";
        public string AmountText => Amount.ToString("C");
        public string OdometerText => $"{OdometerMiles:0,0} mi";
        public string DueMilesText => DueAtMiles.HasValue ? $"{DueAtMiles.Value:0,0} mi" : "";
        public string TruckDisplay =>
            string.IsNullOrWhiteSpace(PlateNumber) ? TruckName : $"{TruckName} • {PlateNumber}";
    }
}