namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetRules
    {
        // Mileage-based service intervals (miles)
        public double OilChangeIntervalMiles { get; set; } = 15000;
        public double TireServiceIntervalMiles { get; set; } = 80000;
        public double MajorServiceIntervalMiles { get; set; } = 120000;

        // DOT inspection interval (days)
        public int DotInspectionIntervalDays { get; set; } = 30;

        // Alert thresholds
        public double FuelLowPct { get; set; } = 15;
        public double DamageWarnPct { get; set; } = 10;
        public double DamageHighPct { get; set; } = 20;

        // “Due soon” window
        public double ServiceDueSoonMiles { get; set; } = 500;
        public int DotDueSoonDays { get; set; } = 5;

        // ✅ Phase 3: auto fuel-fill detection
        public double FuelFillDetectJumpPct { get; set; } = 12; // if fuel jumps by >= 12% we consider it a fill
        public double FuelFillMinMilesSinceLastDetect { get; set; } = 0.2; // avoid false positive at same spot
        public int FuelFillMinMinutesBetweenDetect { get; set; } = 2; // debounce
    }
}