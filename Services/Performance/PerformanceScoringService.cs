using Microsoft.Data.Sqlite;
using System;
using System.Threading.Tasks;

namespace OverWatchELD.Services.Performance
{
    /// <summary>
    /// Simple 0-100 scores from recorded performance events.
    /// Tunable weights and penalties.
    /// </summary>
    public sealed class PerformanceScoringService
    {
        private readonly string _connString;

        public PerformanceScoringService(string connString)
        {
            _connString = connString ?? "";
        }

        public async Task<(int safety, int efficiency, int compliance, int overall)> GetScoreAsync(string driverName, int days)
        {
            if (string.IsNullOrWhiteSpace(_connString))
                return (100, 100, 100, 100);

            driverName = (driverName ?? "").Trim();
            if (driverName.Length == 0)
                return (100, 100, 100, 100);

            days = Math.Clamp(days, 1, 365);

            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT eventType, severity, value
FROM performance_events
WHERE driverName = $d
AND createdUtc >= $since;";
            cmd.Parameters.AddWithValue("$d", driverName);
            cmd.Parameters.AddWithValue("$since", DateTime.UtcNow.AddDays(-days).ToString("O"));

            int safety = 100;
            int efficiency = 100;
            int compliance = 100;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var type = reader.GetString(0);
                var severity = reader.GetInt32(1);

                switch (type)
                {
                    case "HardBrake":
                        safety -= severity * 3;      // sev=2 => -6
                        break;
                    case "HardBrakeSevere":
                        safety -= severity * 4;      // sev=3 => -12
                        break;

                    case "Speeding":
                        safety -= severity * 2;      // sev=2 => -4
                        break;
                    case "SpeedingSevere":
                        safety -= severity * 3;      // sev=3 => -9
                        break;

                    case "Idle":
                        efficiency -= 2;
                        break;

                    case "HosViolation":
                        compliance -= 15;
                        break;
                }
            }

            safety = Math.Clamp(safety, 0, 100);
            efficiency = Math.Clamp(efficiency, 0, 100);
            compliance = Math.Clamp(compliance, 0, 100);

            int overall = (int)(
                safety * 0.45 +
                compliance * 0.35 +
                efficiency * 0.20);

            return (safety, efficiency, compliance, overall);
        }
    }
}
