using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public static class FleetRecordingService
    {
        public static Task RecordTripSegmentAsync(
            string driverId, string driverName,
            string truckId, string truckName,
            DateTime startUtc, DateTime endUtc,
            double distanceMiles)
        {
            return Task.Run(() =>
            {
                using var con = FleetDb.OpenConnection();
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
INSERT INTO FleetTripSegments
(DriverId, DriverName, TruckId, TruckName, StartUtc, EndUtc, DistanceMiles)
VALUES
($dId, $dName, $tId, $tName, $start, $end, $miles);
";
                cmd.Parameters.AddWithValue("$dId", (driverId ?? "").Trim());
                cmd.Parameters.AddWithValue("$dName", (driverName ?? "").Trim());
                cmd.Parameters.AddWithValue("$tId", (truckId ?? "").Trim());
                cmd.Parameters.AddWithValue("$tName", (truckName ?? "").Trim());
                cmd.Parameters.AddWithValue("$start", startUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$end", endUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$miles", distanceMiles);
                cmd.ExecuteNonQuery();
            });
        }

        public static Task RecordFuelEventAsync(
            string driverId, string driverName,
            string truckId, string truckName,
            DateTime utc,
            double gallons,
            double? cost = null,
            double? odometerMiles = null)
        {
            return Task.Run(() =>
            {
                using var con = FleetDb.OpenConnection();
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
INSERT INTO FleetFuelEvents
(DriverId, DriverName, TruckId, TruckName, Utc, Gallons, Cost, OdometerMiles)
VALUES
($dId, $dName, $tId, $tName, $utc, $gal, $cost, $odo);
";
                cmd.Parameters.AddWithValue("$dId", (driverId ?? "").Trim());
                cmd.Parameters.AddWithValue("$dName", (driverName ?? "").Trim());
                cmd.Parameters.AddWithValue("$tId", (truckId ?? "").Trim());
                cmd.Parameters.AddWithValue("$tName", (truckName ?? "").Trim());
                cmd.Parameters.AddWithValue("$utc", utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$gal", gallons);
                cmd.Parameters.AddWithValue("$cost", (object?)cost ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$odo", (object?)odometerMiles ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            });
        }

        public static Task RecordDamageEventAsync(
            string driverId, string driverName,
            string truckId, string truckName,
            DateTime utc,
            double? damagePercent,
            double? damageCost = null,
            string? notes = null)
        {
            return Task.Run(() =>
            {
                using var con = FleetDb.OpenConnection();
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
INSERT INTO FleetDamageEvents
(DriverId, DriverName, TruckId, TruckName, Utc, DamagePercent, DamageCost, Notes)
VALUES
($dId, $dName, $tId, $tName, $utc, $pct, $cost, $notes);
";
                cmd.Parameters.AddWithValue("$dId", (driverId ?? "").Trim());
                cmd.Parameters.AddWithValue("$dName", (driverName ?? "").Trim());
                cmd.Parameters.AddWithValue("$tId", (truckId ?? "").Trim());
                cmd.Parameters.AddWithValue("$tName", (truckName ?? "").Trim());
                cmd.Parameters.AddWithValue("$utc", utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$pct", (object?)damagePercent ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$cost", (object?)damageCost ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$notes", (object?)(notes ?? "").Trim());
                cmd.ExecuteNonQuery();
            });
        }
    }
}