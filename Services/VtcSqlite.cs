using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Small shared SQLite store used by the Discord VTC bot AND the in-app Companion web page (/vtc).
    /// Keeping this separate from ELD logs avoids breaking locked graph/render logic.
    /// </summary>
    public sealed class VtcSqlite
    {
        private readonly string _path;
        public VtcSqlite(string path) => _path = path;

        private SqliteConnection Open() => new($"Data Source={_path}");

        public async Task InitAsync()
        {
            using var con = Open();
            await con.OpenAsync();

            await con.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Drivers(
                discordUserId INTEGER PRIMARY KEY,
                eldDriverId INTEGER NULL,
                displayName TEXT NULL,
                username TEXT NULL,
                role TEXT NULL,
                lastSeenUtc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS DispatchMessages(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                eldDriverId INTEGER NOT NULL,
                fromDiscordUserId INTEGER NOT NULL,
                fromName TEXT NOT NULL,
                text TEXT NOT NULL,
                dateUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Expenses(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                eldDriverId INTEGER NOT NULL,
                driverName TEXT NOT NULL,
                type TEXT NOT NULL,
                amount REAL NOT NULL,
                note TEXT NULL,
                dateUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Maintenance(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                truck TEXT NOT NULL,
                type TEXT NOT NULL,
                miles INTEGER NULL,
                cost REAL NULL,
                note TEXT NULL,
                dateUtc TEXT NOT NULL
            );
            ");
        }

        public async Task<int> CountActiveDrivers24hAsync()
        {
            using var con = Open();
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24).ToString("O");
            return await con.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Drivers WHERE lastSeenUtc >= @cutoff", new { cutoff });
        }

        public async Task<double> SumCostsLastDaysAsync(int days)
        {
            using var con = Open();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToString("O");
            return await con.ExecuteScalarAsync<double?>("SELECT SUM(amount) FROM Expenses WHERE dateUtc >= @cutoff", new { cutoff }) ?? 0;
        }

        public async Task<int> CountDispatchLastDaysAsync(int days)
        {
            using var con = Open();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToString("O");
            return await con.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM DispatchMessages WHERE dateUtc >= @cutoff", new { cutoff });
        }

        public async Task<int> CountMaintenanceLastDaysAsync(int days)
        {
            using var con = Open();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToString("O");
            return await con.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Maintenance WHERE dateUtc >= @cutoff", new { cutoff });
        }

        public async Task<IEnumerable<object>> GetEmployeesAsync()
        {
            using var con = Open();
            var rows = await con.QueryAsync(@"
            SELECT discordUserId, eldDriverId, displayName, username, role, lastSeenUtc
            FROM Drivers
            ORDER BY COALESCE(displayName, username) ASC;
            ");
            return rows.Select(r => (object)r);
        }

        public async Task<IEnumerable<object>> GetCostsAsync(int days)
        {
            using var con = Open();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToString("O");
            var rows = await con.QueryAsync(@"
            SELECT dateUtc, driverName, type, amount, note
            FROM Expenses
            WHERE dateUtc >= @cutoff
            ORDER BY dateUtc DESC;
            ", new { cutoff });
            return rows.Select(r => (object)r);
        }

        public async Task<IEnumerable<object>> GetMaintenanceAsync(int days)
        {
            using var con = Open();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToString("O");
            var rows = await con.QueryAsync(@"
            SELECT dateUtc, truck, type, miles, cost, note
            FROM Maintenance
            WHERE dateUtc >= @cutoff
            ORDER BY dateUtc DESC;
            ", new { cutoff });
            return rows.Select(r => (object)r);
        }
    }
}
