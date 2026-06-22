using System;
using System.Collections.Concurrent;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Live fleet telemetry processor.
    /// Called from TelemetryService every poll tick.
    /// Aggregates per-driver, per-truck stats safely.
    /// </summary>
    public static class FleetAutoLoggerService
    {
        private sealed class DriverTruckState
        {
            public double? LastOdometer;
            public double? LastFuel;
            public double? LastDamagePct;
            public DateTimeOffset LastSeenUtc;
        }

        private static readonly ConcurrentDictionary<string, DriverTruckState> _state
            = new ConcurrentDictionary<string, DriverTruckState>();

        /// <summary>
        /// Damage increase threshold to record spike (in %).
        /// </summary>
        public static double DamageSpikeThresholdPct { get; set; } = 1.0;

        public static void ProcessTelemetrySnapshot(TelemetrySnapshot snapshot)
        {
            if (snapshot == null) return;
            if (!snapshot.Connected) return;
            if (string.IsNullOrWhiteSpace(snapshot.DriverId)) return;
            if (string.IsNullOrWhiteSpace(snapshot.TruckId)) return;

            var key = $"{snapshot.DriverId}::{snapshot.TruckId}";
            var state = _state.GetOrAdd(key, _ => new DriverTruckState());

            try
            {
                HandleMileage(snapshot, state);
                HandleFuel(snapshot, state);
                HandleDamage(snapshot, state);

                state.LastSeenUtc = snapshot.SeenUtc;
            }
            catch
            {
                // fail silently — never break telemetry loop
            }
        }

        private static void HandleMileage(TelemetrySnapshot snapshot, DriverTruckState state)
        {
            if (!snapshot.OdometerMiles.HasValue)
                return;

            var currentOdo = snapshot.OdometerMiles.Value;

            if (state.LastOdometer.HasValue)
            {
                var delta = currentOdo - state.LastOdometer.Value;

                // ignore negative or insane jumps
                if (delta > 0 && delta < 500)
                {
                    FleetStatsStore.AddMileage(
                        snapshot.DriverId!,
                        snapshot.DriverName ?? "Driver",
                        snapshot.TruckId!,
                        snapshot.TruckName ?? "Truck",
                        delta
                    );
                }
            }

            state.LastOdometer = currentOdo;
        }

        private static void HandleFuel(TelemetrySnapshot snapshot, DriverTruckState state)
        {
            if (!snapshot.FuelGallons.HasValue)
                return;

            var currentFuel = snapshot.FuelGallons.Value;

            if (state.LastFuel.HasValue)
            {
                var delta = state.LastFuel.Value - currentFuel;

                // Fuel consumed (ignore refuel increases)
                if (delta > 0 && delta < 300)
                {
                    FleetStatsStore.AddFuelUsed(
                        snapshot.DriverId!,
                        snapshot.TruckId!,
                        delta
                    );
                }
            }

            state.LastFuel = currentFuel;
        }

        private static void HandleDamage(TelemetrySnapshot snapshot, DriverTruckState state)
        {
            if (!snapshot.DamagePct.HasValue)
                return;

            var damage = snapshot.DamagePct.Value;

            // Normalize 0..1 to 0..100 if needed
            if (damage <= 1.001)
                damage *= 100.0;

            if (state.LastDamagePct.HasValue)
            {
                var delta = damage - state.LastDamagePct.Value;

                if (delta >= DamageSpikeThresholdPct)
                {
                    FleetStatsStore.RecordDamageSpike(
                        snapshot.DriverId!,
                        snapshot.TruckId!,
                        delta,
                        damage
                    );

                    try
                    {
                        var truck = string.IsNullOrWhiteSpace(snapshot.TruckName) ? (snapshot.TruckId ?? "Truck") : snapshot.TruckName!;
                        DashboardToastService.Malfunction(truck, $"Damage increased by {delta:0.0}% (now {damage:0.0}%).");
                    }
                    catch
                    {
                    }
                }
            }

            state.LastDamagePct = damage;
        }

        /// <summary>
        /// Force-flush current telemetry state.
        /// Safe to call on shutdown.
        /// </summary>
        public static void FlushNow()
        {
            // Nothing buffered currently, but hook reserved for expansion
        }

        internal static void OnTelemetry(TelemetrySnapshot snapshot)
        {
            ProcessTelemetrySnapshot(snapshot);
        }
    }
}