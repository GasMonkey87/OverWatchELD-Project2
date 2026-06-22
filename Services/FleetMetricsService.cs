using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class DriverFleetStats
    {
        public string DriverId { get; init; } = "";
        public string DriverName { get; init; } = "";

        public double Miles { get; init; }
        public double Gallons { get; init; }
        public double Mpg => Gallons > 0 ? Miles / Gallons : 0;

        public int TrucksUsed { get; init; }
        public int Incidents { get; init; }

        public double DamageCost { get; init; }
        public double MaxDamagePercent { get; init; }
    }

    public static class FleetMetricsService
    {
        public static Task<List<DriverFleetStats>> GetDriverStatsAsync(DateTime fromUtc, DateTime toUtc)
        {
            return Task.Run(() =>
            {
                var list = new List<DriverFleetStats>();

                using var con = FleetDb.OpenConnection();

                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
SELECT
  COALESCE(d.DriverId, '') AS DriverId,
  COALESCE(d.DriverName, '') AS DriverName,

  -- miles
  COALESCE((SELECT SUM(t.DistanceMiles) FROM FleetTripSegments t
            WHERE t.DriverId = d.DriverId AND t.StartUtc >= $fromUtc AND t.StartUtc < $toUtc), 0) AS Miles,

  -- fuel gallons
  COALESCE((SELECT SUM(f.Gallons) FROM FleetFuelEvents f
            WHERE f.DriverId = d.DriverId AND f.Utc >= $fromUtc AND f.Utc < $toUtc), 0) AS Gallons,

  -- distinct trucks used (from trip segments, fuel events, damage events)
  COALESCE((SELECT COUNT(DISTINCT x.TruckId) FROM (
        SELECT TruckId FROM FleetTripSegments WHERE DriverId = d.DriverId AND StartUtc >= $fromUtc AND StartUtc < $toUtc
        UNION
        SELECT TruckId FROM FleetFuelEvents    WHERE DriverId = d.DriverId AND Utc      >= $fromUtc AND Utc      < $toUtc
        UNION
        SELECT TruckId FROM FleetDamageEvents  WHERE DriverId = d.DriverId AND Utc      >= $fromUtc AND Utc      < $toUtc
  ) x), 0) AS TrucksUsed,

  -- incidents
  COALESCE((SELECT COUNT(1) FROM FleetDamageEvents x
            WHERE x.DriverId = d.DriverId AND x.Utc >= $fromUtc AND x.Utc < $toUtc), 0) AS Incidents,

  -- damage cost
  COALESCE((SELECT SUM(COALESCE(x.DamageCost,0)) FROM FleetDamageEvents x
            WHERE x.DriverId = d.DriverId AND x.Utc >= $fromUtc AND x.Utc < $toUtc), 0) AS DamageCost,

  -- max damage percent
  COALESCE((SELECT MAX(COALESCE(x.DamagePercent,0)) FROM FleetDamageEvents x
            WHERE x.DriverId = d.DriverId AND x.Utc >= $fromUtc AND x.Utc < $toUtc), 0) AS MaxDamagePercent

FROM (
  -- “drivers present in range” anchor set
  SELECT DriverId, MAX(COALESCE(DriverName,'')) AS DriverName FROM FleetTripSegments
    WHERE StartUtc >= $fromUtc AND StartUtc < $toUtc GROUP BY DriverId
  UNION
  SELECT DriverId, MAX(COALESCE(DriverName,'')) AS DriverName FROM FleetFuelEvents
    WHERE Utc >= $fromUtc AND Utc < $toUtc GROUP BY DriverId
  UNION
  SELECT DriverId, MAX(COALESCE(DriverName,'')) AS DriverName FROM FleetDamageEvents
    WHERE Utc >= $fromUtc AND Utc < $toUtc GROUP BY DriverId
) d
ORDER BY Miles DESC;
";

                cmd.Parameters.AddWithValue("$fromUtc", fromUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$toUtc", toUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new DriverFleetStats
                    {
                        DriverId = r.GetString(0),
                        DriverName = r.GetString(1),
                        Miles = r.GetDouble(2),
                        Gallons = r.GetDouble(3),
                        TrucksUsed = r.GetInt32(4),
                        Incidents = r.GetInt32(5),
                        DamageCost = r.GetDouble(6),
                        MaxDamagePercent = r.GetDouble(7),
                    });
                }

                return list;
            });
        }
    }
}