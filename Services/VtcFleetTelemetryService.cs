using System;
using OverWatchELD.Services.Fleet;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Phase 2: Tracks current fleet status + mileage deltas for the *linked* driver
    /// using telemetry updates. Writes into OverWatchELD.db (DatabaseService).
    /// </summary>
    public sealed class VtcFleetTelemetryService
    {
        private readonly TelemetryService _telemetry;

        public VtcFleetTelemetryService(TelemetryService telemetry)
        {
            _telemetry = telemetry;
            _telemetry.Updated += OnTelemetry;
        }

        private void OnTelemetry(TelemetrySnapshot snap)
        {
            try
            {
                if (snap == null) return;
                if (!snap.Connected) return;

                var l = VtcLinkService.GetLink();
                if (l == null || !l.Linked) return;

                var uid = l.DiscordUserId;
                if (string.IsNullOrWhiteSpace(uid)) return;

                // Update fleet status (best-effort)
                DatabaseService.UpsertVtcVehicleStatus(new DatabaseService.VtcVehicleStatus
                {
                    DiscordUserId = uid,
                    DriverName = l.DriverName ?? "Driver",
                    TruckMakeModel = snap.TruckMakeModel,
                    OdometerMiles = snap.OdometerMiles,
                    FuelPct = snap.FuelPct,
                    DamagePct = snap.DamagePct,
                    City = snap.City,
                    State = snap.State,
                    UpdatedUtc = snap.SeenUtc
                });

                // Website portal fleet sync: push current telemetry truck to the VTC portal.
                try
                {
                    FleetPortalSyncService.Shared.QueueTelemetrySync(snap);
                }
                catch
                {
                    // Never break telemetry loop because website sync failed.
                }

                // Track mileage (only when we have odometer)
                if (snap.OdometerMiles.HasValue)
                {
                    DatabaseService.AddMileageFromOdometer(
                        discordUserId: uid,
                        driverName: l.DriverName ?? "Driver",
                        odometerMiles: snap.OdometerMiles.Value,
                        utcNow: snap.SeenUtc == default ? DateTimeOffset.UtcNow : snap.SeenUtc
                    );
                }
            }
            catch
            {
                // never break telemetry loop
            }
        }
    }
}
