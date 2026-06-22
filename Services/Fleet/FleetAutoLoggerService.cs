using System;
using OverWatchELD.Services;
using OverWatchELD.Models.Fleet;

namespace OverWatchELD.Services.Fleet
{
    /// <summary>
    /// Phase 2 + Phase 3 Fleet Auto Logger
    /// - Reads ActivePlate from FleetIdentityStore
    /// - Upserts truck + pushes odometer/fuel/damage from TelemetrySnapshot
    /// - Throttles writes to avoid saving 4x/sec at 250ms polling
    /// - FlushNow() forces a final write on stop (already called by TelemetryService.Stop())
    /// </summary>
    public static class FleetAutoLoggerService
    {
        // Shared store/rules so PC + companion share same data location
        private static readonly FleetStore _store = new FleetStore();
        private static readonly FleetRules _rules = new FleetRules();
        private static readonly FleetMaintenanceService _fleet = new FleetMaintenanceService(_store, _rules);

        private static readonly FleetIdentityStore _identity = new FleetIdentityStore();

        // Throttle
        private static DateTimeOffset _lastWriteUtc = DateTimeOffset.MinValue;
        private static TelemetrySnapshot? _lastSnapshot;
        private static readonly object _lock = new object();

        // Write at most once per X ms unless a major change happens
        private const int MinWriteMs = 1100;

        // If odometer jumps this much since last write, write immediately
        private const double OdoImmediateDeltaMiles = 0.05; // ~260 ft

        // If fuel changes this much, write immediately
        private const double FuelImmediateDeltaPct = 1.5; // percent points

        public static void ProcessTelemetrySnapshot(TelemetrySnapshot snap)
        {
            if (snap == null) return;

            lock (_lock)
            {
                _lastSnapshot = snap;

                // Only log when connected + we have at least odometer OR fuel OR damage
                if (!snap.Connected) return;

                var hasAny =
                    snap.OdometerMiles.HasValue ||
                    snap.FuelPct.HasValue ||
                    snap.DamagePct.HasValue;

                if (!hasAny) return;

                // Resolve active plate (license plate / unit number)
                var plate = (_identity.LoadActivePlate() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(plate))
                {
                    // If user hasn't set an active plate yet, don’t spam data into UNIT-001 silently.
                    // But we *do* allow logging if they already have a truck in store named UNIT-001.
                    // If not, just no-op until they set ActivePlate from Fleet UI.
                    return;
                }

                // Throttle decision
                var now = DateTimeOffset.UtcNow;
                var allow = ShouldWriteNow(now, plate, snap);
                if (!allow) return;

                WriteNow(now, plate, snap);
            }
        }

        public static void FlushNow()
        {
            lock (_lock)
            {
                if (_lastSnapshot == null) return;

                var snap = _lastSnapshot;

                if (!snap.Connected) return;

                var plate = (_identity.LoadActivePlate() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(plate)) return;

                WriteNow(DateTimeOffset.UtcNow, plate, snap);
            }
        }

        // ------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------
        private static bool ShouldWriteNow(DateTimeOffset nowUtc, string plate, TelemetrySnapshot snap)
        {
            // First write always
            if (_lastWriteUtc == DateTimeOffset.MinValue) return true;

            var ms = (nowUtc - _lastWriteUtc).TotalMilliseconds;
            if (ms >= MinWriteMs) return true;

            // If big delta since last snapshot write, write sooner
            var t = _fleet.GetByPlate(plate);

            // If the truck doesn't exist yet, write now to create it
            if (t == null) return true;

            if (snap.OdometerMiles.HasValue)
            {
                var odo = snap.OdometerMiles.Value;
                if (Math.Abs(odo - t.OdometerMiles) >= OdoImmediateDeltaMiles) return true;
            }

            if (snap.FuelPct.HasValue)
            {
                var fuelPct100 = ClampPct100(snap.FuelPct.Value * 100.0);
                if (Math.Abs(fuelPct100 - t.FuelPct) >= FuelImmediateDeltaPct) return true;
            }

            // Otherwise throttle
            return false;
        }

        private static void WriteNow(DateTimeOffset nowUtc, string plate, TelemetrySnapshot snap)
        {
            try
            {
                // Truck name/make-model
                var makeModel = (snap.TruckMakeModel ?? snap.TruckName ?? snap.TruckId ?? "").Trim();

                // Odometer
                var odo = snap.OdometerMiles ?? 0;

                // Fuel (0..1 -> 0..100)
                var fuelPct = snap.FuelPct.HasValue ? ClampPct100(snap.FuelPct.Value * 100.0) : double.NaN;

                // Damage (0..1 -> 0..100)
                var dmgPct = snap.DamagePct.HasValue ? ClampPct100(snap.DamagePct.Value * 100.0) : double.NaN;

                // Upsert truck so it exists
                _fleet.UpsertTruck(plate, makeModel);

                // If we only have aggregate damage, mirror it across components
                // (later, if you capture per-part wear from Funbit, you can map it here)
                var engine = double.IsNaN(dmgPct) ? 0 : dmgPct;
                var trans = double.IsNaN(dmgPct) ? 0 : dmgPct;
                var cabin = double.IsNaN(dmgPct) ? 0 : dmgPct;
                var chassis = double.IsNaN(dmgPct) ? 0 : dmgPct;
                var wheels = double.IsNaN(dmgPct) ? 0 : dmgPct;

                // If fuel missing, keep last known
                if (double.IsNaN(fuelPct))
                {
                    var existing = _fleet.GetByPlate(plate);
                    fuelPct = existing?.FuelPct ?? 0;
                }

                _fleet.UpdateFromTelemetry(
                    plate: plate,
                    odometerMiles: odo,
                    fuelPct: fuelPct,
                    engineDmgPct: engine,
                    transDmgPct: trans,
                    cabinDmgPct: cabin,
                    chassisDmgPct: chassis,
                    wheelsDmgPct: wheels
                );

                _lastWriteUtc = nowUtc;
            }
            catch
            {
                // never crash telemetry loop
            }
        }

        private static double ClampPct100(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }
    }
}