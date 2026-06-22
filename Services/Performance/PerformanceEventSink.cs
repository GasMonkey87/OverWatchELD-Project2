using Microsoft.Data.Sqlite;
using System;
using System.Threading.Tasks;

namespace OverWatchELD.Services.Performance
{
    public sealed class PerformanceEventSink
    {
        private readonly string _connString;

        public PerformanceEventSink(string connString)
        {
            _connString = connString ?? "";
        }

        public async Task RecordAsync(string driverName, string type, int severity, double value)
        {
            if (string.IsNullOrWhiteSpace(_connString)) return;
            driverName = (driverName ?? "").Trim();
            type = (type ?? "").Trim();
            if (driverName.Length == 0 || type.Length == 0) return;

            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO performance_events
(driverName, eventType, severity, value, createdUtc)
VALUES ($d,$t,$s,$v,$u);";
            cmd.Parameters.AddWithValue("$d", driverName);
            cmd.Parameters.AddWithValue("$t", type);
            cmd.Parameters.AddWithValue("$s", severity);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
