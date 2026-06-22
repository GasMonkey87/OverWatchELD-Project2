using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace OverWatchELD.Services
{
    public static class FleetDb
    {
        private static string? _dbPathOverride;

        /// <summary>
        /// Optional override if you already have a known DB path (set at startup).
        /// </summary>
        public static void SetDbPath(string dbPath)
        {
            _dbPathOverride = dbPath;
        }

        public static string GetDbPath()
        {
            if (!string.IsNullOrWhiteSpace(_dbPathOverride))
                return _dbPathOverride!;

            // Try common locations first
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates =
            {
                Path.Combine(baseDir, "OverWatchELD.db"),
                Path.Combine(baseDir, "eld.db"),
                Path.Combine(baseDir, "db.sqlite"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "OverWatchELD.db"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverWatchELD", "OverWatchELD.db"),
            };

            foreach (var c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return c;
                }
                catch { }
            }

            // Default create in AppData
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "OverWatchELD.db");
        }

        public static SqliteConnection OpenConnection()
        {
            var path = GetDbPath();
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            var con = new SqliteConnection(cs);
            con.Open();

            EnsureSchema(con);
            return con;
        }

        private static void EnsureSchema(SqliteConnection con)
        {
            // Trip segments (mileage by driver+truck)
            Exec(con, @"
CREATE TABLE IF NOT EXISTS FleetTripSegments (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  DriverId TEXT NOT NULL,
  DriverName TEXT NULL,
  TruckId TEXT NOT NULL,
  TruckName TEXT NULL,
  StartUtc TEXT NOT NULL,
  EndUtc TEXT NOT NULL,
  DistanceMiles REAL NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_FleetTripSegments_DriverTime ON FleetTripSegments(DriverId, StartUtc);
CREATE INDEX IF NOT EXISTS IX_FleetTripSegments_TruckTime ON FleetTripSegments(TruckId, StartUtc);
");

            // Fuel events (fuel usage by driver+truck)
            Exec(con, @"
CREATE TABLE IF NOT EXISTS FleetFuelEvents (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  DriverId TEXT NOT NULL,
  DriverName TEXT NULL,
  TruckId TEXT NOT NULL,
  TruckName TEXT NULL,
  Utc TEXT NOT NULL,
  Gallons REAL NOT NULL DEFAULT 0,
  Cost REAL NULL,
  OdometerMiles REAL NULL
);
CREATE INDEX IF NOT EXISTS IX_FleetFuelEvents_DriverTime ON FleetFuelEvents(DriverId, Utc);
");

            // Damage events (damage by driver+truck)
            Exec(con, @"
CREATE TABLE IF NOT EXISTS FleetDamageEvents (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  DriverId TEXT NOT NULL,
  DriverName TEXT NULL,
  TruckId TEXT NOT NULL,
  TruckName TEXT NULL,
  Utc TEXT NOT NULL,
  DamagePercent REAL NULL,
  DamageCost REAL NULL,
  Notes TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_FleetDamageEvents_DriverTime ON FleetDamageEvents(DriverId, Utc);
");
        }

        private static void Exec(SqliteConnection con, string sql)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}