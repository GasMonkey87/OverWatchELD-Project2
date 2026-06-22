using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class DatabaseService
    {
        private static readonly object _lock = new();
        private static string _dbPath = "";
        private static bool _initialized;

        private static string ConnectionString
            => new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();

        private static string DefaultDbPath =>
    AppPaths.FileInData("OverWatchELD.db");

        // -----------------------------
        // ✅ DB location compatibility / migration
        // Older builds sometimes stored the DB under a different folder name.
        // If the new build runs from a new folder and can't find prior data, clocks can show 00:00.
        // This copies an existing legacy DB into the current expected location on first run.
        // -----------------------------
        private static void TryMigrateLegacyDb(string targetDbPath, string targetDir)
        {
            try
            {
                if (File.Exists(targetDbPath)) return;

                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var baseDir = AppContext.BaseDirectory;

                var candidates = new[]
                {
            Path.Combine(baseDir, "OverWatchELD.db"),
            Path.Combine(baseDir, "Data", "OverWatchELD.db"),

            Path.Combine(local, "OverWatchELD", "OverWatchELD.db"),
            Path.Combine(local, "ATS_ELD", "ATS_ELD.db"),
            Path.Combine(roam,  "OverWatchELD", "OverWatchELD.db"),
            Path.Combine(roam,  "ATS_ELD", "OverWatchELD.db"),

            Path.Combine(local, "OverWatchELD.db"),
            Path.Combine(roam,  "OverWatchELD.db"),
        };

                var src = candidates.FirstOrDefault(File.Exists);
                if (string.IsNullOrWhiteSpace(src)) return;

                Directory.CreateDirectory(targetDir);
                File.Copy(src, targetDbPath, overwrite: false);
            }
            catch
            {
                // ignore migration failures; app will create a fresh DB
            }
        }

        public static void InsertHosClockResetEvent()
        {
            InsertDutyEvent(new DutyEvent
            {
                StartUtc = DateTimeOffset.UtcNow,
                Status = DutyStatus.OffDuty,
                Location = "Clocks Reset",
                Notes = "Clocks Reset Used",
                Source = "system",
                IsEdited = true,
                EditedAtUtc = DateTimeOffset.UtcNow,
                EditReason = "Driver requested HOS clock reset"
            });
        }

        public static DateTimeOffset? GetLatestHosClockResetUtc()
        {
            EnsureInit();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
        SELECT start_utc
        FROM duty_events
        WHERE notes = 'Clocks Reset Used'
           OR edit_reason = 'Driver requested HOS clock reset'
        ORDER BY start_utc DESC
        LIMIT 1;
        """;

            var result = cmd.ExecuteScalar()?.ToString();

            if (string.IsNullOrWhiteSpace(result))
                return null;

            return DateTimeOffset.Parse(result).ToUniversalTime();
        }

        public static void Initialize(string? dbPath = null)
        {
            lock (_lock)
            {
                if (_initialized) return;

                _dbPath = string.IsNullOrWhiteSpace(dbPath)
                    ? DefaultDbPath
                    : dbPath;

                Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

                // Pull forward an older DB into the new program-folder data path.
                TryMigrateLegacyDb(_dbPath, Path.GetDirectoryName(_dbPath)!);

                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        """
                        CREATE TABLE IF NOT EXISTS duty_events (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            start_utc TEXT NOT NULL,
                            end_utc TEXT,
                            status INTEGER NOT NULL,
                            location_text TEXT,
                            notes TEXT,
                            lat REAL,
                            lon REAL,
                            source TEXT,
                            is_edited INTEGER DEFAULT 0,
                            edited_at_utc TEXT,
                            edit_reason TEXT
                        );
                        """;
                    cmd.ExecuteNonQuery();
                }

                // Daily log certifications (local log day)
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        """
                        CREATE TABLE IF NOT EXISTS daily_log_certifications (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            log_date_local TEXT NOT NULL UNIQUE, -- yyyy-MM-dd (local date)
                            signed_at_utc TEXT NOT NULL,
                            driver_name TEXT,
                            signature TEXT,
                            certified INTEGER NOT NULL DEFAULT 1,
                            certification_text TEXT
                        );
                        """;
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        """
                        CREATE TABLE IF NOT EXISTS inspections (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            created_utc TEXT NOT NULL,
                            type TEXT,
                            vehicle_id TEXT,
                            notes TEXT,
                            signature TEXT
                        );
CREATE TABLE IF NOT EXISTS inspection_item_results (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    inspection_id INTEGER NOT NULL,
    item_key TEXT NOT NULL,
    item_name TEXT NOT NULL,
    category TEXT NOT NULL,
    status TEXT NOT NULL, -- 'ok' | 'defect'
    note TEXT,
    FOREIGN KEY (inspection_id) REFERENCES inspections(id) ON DELETE CASCADE
);
""";
                    cmd.ExecuteNonQuery();
                }


// VTC Fleet + Mileage (Phase 2)
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText =
        """
        CREATE TABLE IF NOT EXISTS vtc_vehicle_status(
            discord_user_id TEXT PRIMARY KEY,
            driver_name TEXT NOT NULL,
            truck_make_model TEXT,
            odometer_miles REAL,
            fuel_pct REAL,
            damage_pct REAL,
            city TEXT,
            state TEXT,
            updated_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS vtc_mileage_state(
            discord_user_id TEXT PRIMARY KEY,
            last_odometer_miles REAL,
            updated_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS vtc_mileage_daily(
            discord_user_id TEXT NOT NULL,
            driver_name TEXT NOT NULL,
            day_local TEXT NOT NULL,        -- YYYY-MM-DD in local time
            miles REAL NOT NULL,
            updated_utc TEXT NOT NULL,
            PRIMARY KEY(discord_user_id, day_local)
        );
        """;
    cmd.ExecuteNonQuery();
}

                _initialized = true;
            }
        }

        private static void EnsureInit()
        {
            if (!_initialized) Initialize();
        }

        // ==========================
        // DUTY EVENTS (timeline-safe)
        // ==========================

        public static List<DutyEvent> GetDutyEvents(DateTimeOffset startUtc, DateTimeOffset endUtc)
        {
            EnsureInit();
            var list = new List<DutyEvent>();

            var startIso = startUtc.UtcDateTime.ToString("o");
            var endIso = endUtc.UtcDateTime.ToString("o");

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, start_utc, end_utc, status, location_text, notes, lat, lon, source, is_edited, edited_at_utc, edit_reason
                FROM duty_events
                WHERE start_utc < $end
                  AND (end_utc IS NULL OR end_utc > $start)
                ORDER BY start_utc ASC;";
            cmd.Parameters.AddWithValue("$start", startIso);
            cmd.Parameters.AddWithValue("$end", endIso);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var start = DateTimeOffset.Parse(reader.GetString(1)).ToUniversalTime();
                DateTimeOffset? end = null;
                if (!reader.IsDBNull(2))
                    end = DateTimeOffset.Parse(reader.GetString(2)).ToUniversalTime();

                list.Add(new DutyEvent
                {
                    Id = reader.GetInt64(0),
                    StartUtc = start,
                    EndUtc = end,
                    Status = (DutyStatus)reader.GetInt32(3),
                    Location = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Lat = reader.IsDBNull(6) ? (double?)null : reader.GetDouble(6),
                    Lon = reader.IsDBNull(7) ? (double?)null : reader.GetDouble(7),
                    Source = reader.IsDBNull(8) ? null : reader.GetString(8),
                    IsEdited = !reader.IsDBNull(9) && reader.GetInt32(9) != 0,
                    EditedAtUtc = reader.IsDBNull(10) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(10)).ToUniversalTime(),
                    EditReason = reader.IsDBNull(11) ? null : reader.GetString(11),
                });
            }

            return list;
        }

        /// <summary>
        /// Inserts a duty event at dutyEvent.StartUtc and guarantees the timeline never overlaps.
        /// If the timestamp falls inside an existing event, that event is split.
        /// If an event already starts exactly at this timestamp, it is updated instead of inserting a duplicate.
        /// </summary>
        public static long InsertDutyEvent(DutyEvent dutyEvent)
        {
            EnsureInit();

            var tUtc = dutyEvent.StartUtc.ToUniversalTime();
            var tIso = tUtc.UtcDateTime.ToString("o");

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            // Load the event that overlaps t (if any): start < t < end (or open)
            var overlap = ReadOverlappingEvent(conn, tx, tIso);

            // Load the next event starting at or after t
            var next = ReadNextEvent(conn, tx, tIso);

            // If an event already starts exactly at t, update it (Motive-style)
            if (next != null && string.Equals(next.Value.StartIso, tIso, StringComparison.Ordinal))
            {
                // Close overlap (if it started before t and is still open/extends past t)
                if (overlap != null && overlap.Value.Id != next.Value.Id)
                    CloseEventAt(conn, tx, overlap.Value.Id, tIso);

                // Update existing start-at-t event to the new status + fields
                UpdateEventRow(conn, tx, next.Value.Id, dutyEvent, startIso: tIso, endIso: next.Value.EndIso);
                NormalizeAround(conn, tx, next.Value.Id);
                tx.Commit();
                return next.Value.Id;
            }

            // Determine end for new event:
            // - if we are splitting an overlap, new event inherits overlap's old end
            // - but we also must clamp to the next event start
            string? newEndIso = null;
            if (overlap != null)
            {
                // If overlap starts exactly at t, just update it
                if (string.Equals(overlap.Value.StartIso, tIso, StringComparison.Ordinal))
                {
                    UpdateEventRow(conn, tx, overlap.Value.Id, dutyEvent, startIso: overlap.Value.StartIso, endIso: overlap.Value.EndIso);
                    NormalizeAround(conn, tx, overlap.Value.Id);
                    tx.Commit();
                    return overlap.Value.Id;
                }

                newEndIso = overlap.Value.EndIso; // may be null (open)
            }

            if (next != null)
            {
                // Clamp end to next start so we never overlap the next segment
                if (newEndIso == null || string.CompareOrdinal(next.Value.StartIso, newEndIso) < 0)
                    newEndIso = next.Value.StartIso;
            }

            // Split the overlap event: its end becomes t
            if (overlap != null)
            {
                CloseEventAt(conn, tx, overlap.Value.Id, tIso);
            }
            else
            {
                // If there is an open-ended last event that started before t, close it (safer for older databases)
                CloseMostRecentOpenBefore(conn, tx, tIso);
            }

            // Insert new event with calculated end
            var newId = InsertEventRow(conn, tx, dutyEvent, tIso, newEndIso);

            // Clean up (merge adjacent same status, remove zero-length)
            NormalizeAround(conn, tx, newId);

            tx.Commit();
            return newId;
        }

        public static void UpdateDutyEvent(DutyEvent dutyEvent)
        {
            EnsureInit();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                UPDATE duty_events
                SET start_utc = $start,
                    end_utc = $end,
                    status = $status,
                    location_text = $location,
                    notes = $notes,
                    lat = $lat,
                    lon = $lon,
                    source = $source,
                    is_edited = $isEdited,
                    edited_at_utc = $editedAt,
                    edit_reason = $editReason
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", dutyEvent.Id);
            cmd.Parameters.AddWithValue("$start", dutyEvent.StartUtc.UtcDateTime.ToString("o"));
            cmd.Parameters.AddWithValue("$end", (object?)dutyEvent.EndUtc?.UtcDateTime.ToString("o") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", (int)dutyEvent.Status);
            cmd.Parameters.AddWithValue("$location", (object?)dutyEvent.LocationText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$notes", (object?)dutyEvent.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lat", (object?)dutyEvent.Lat ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lon", (object?)dutyEvent.Lon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$source", (object?)dutyEvent.Source ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$isEdited", dutyEvent.IsEdited ? 1 : 0);
            cmd.Parameters.AddWithValue("$editedAt", (object?)dutyEvent.EditedAtUtc?.UtcDateTime.ToString("o") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$editReason", (object?)dutyEvent.EditReason ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public static void DeleteDutyEvent(long id)
        {
            EnsureInit();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM duty_events WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        public static void CloseOpenDutyEvent(DateTimeOffset endUtc)
        {
            EnsureInit();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                UPDATE duty_events
                SET end_utc = $end
                WHERE id = (
                    SELECT id FROM duty_events
                    WHERE end_utc IS NULL
                    ORDER BY start_utc DESC
                    LIMIT 1
                );
                """;
            cmd.Parameters.AddWithValue("$end", endUtc.UtcDateTime.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public static void DeleteAllDutyEvents()
        {
            EnsureInit();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM duty_events;";
            cmd.ExecuteNonQuery();
        }

        // --------------------------
        // Internal: timeline helpers
        // --------------------------

        private readonly struct RowRef
        {
            public RowRef(long id, string startIso, string? endIso)
            {
                Id = id;
                StartIso = startIso;
                EndIso = endIso;
            }
            public long Id { get; }
            public string StartIso { get; }
            public string? EndIso { get; }
        }

        private static RowRef? ReadOverlappingEvent(SqliteConnection conn, SqliteTransaction tx, string tIso)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                @"SELECT id, start_utc, end_utc
                  FROM duty_events
                  WHERE start_utc < $t
                    AND (end_utc IS NULL OR end_utc > $t)
                  ORDER BY start_utc DESC
                  LIMIT 1;";
            cmd.Parameters.AddWithValue("$t", tIso);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            var id = r.GetInt64(0);
            var startIso = r.GetString(1);
            string? endIso = r.IsDBNull(2) ? null : r.GetString(2);
            return new RowRef(id, startIso, endIso);
        }

        private static RowRef? ReadNextEvent(SqliteConnection conn, SqliteTransaction tx, string tIso)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                @"SELECT id, start_utc, end_utc
                  FROM duty_events
                  WHERE start_utc >= $t
                  ORDER BY start_utc ASC
                  LIMIT 1;";
            cmd.Parameters.AddWithValue("$t", tIso);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            var id = r.GetInt64(0);
            var startIso = r.GetString(1);
            string? endIso = r.IsDBNull(2) ? null : r.GetString(2);
            return new RowRef(id, startIso, endIso);
        }

        private static void CloseEventAt(SqliteConnection conn, SqliteTransaction tx, long id, string endIso)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                @"UPDATE duty_events
                  SET end_utc = $end
                  WHERE id = $id
                    AND (end_utc IS NULL OR end_utc > $end);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$end", endIso);
            cmd.ExecuteNonQuery();
        }

        private static void CloseMostRecentOpenBefore(SqliteConnection conn, SqliteTransaction tx, string endIso)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                @"UPDATE duty_events
                  SET end_utc = $end
                  WHERE id = (
                      SELECT id FROM duty_events
                      WHERE end_utc IS NULL
                        AND start_utc < $end
                      ORDER BY start_utc DESC
                      LIMIT 1
                  );";
            cmd.Parameters.AddWithValue("$end", endIso);
            cmd.ExecuteNonQuery();
        }

        private static long InsertEventRow(SqliteConnection conn, SqliteTransaction tx, DutyEvent dutyEvent, string startIso, string? endIso)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO duty_events(start_utc, end_utc, status, location_text, notes, lat, lon, source, is_edited, edited_at_utc, edit_reason) " +
                "VALUES($startUtc, $endUtc, $status, $locText, $notes, $lat, $lon, $source, $isEdited, $editedAt, $editReason); " +
                "SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$startUtc", startIso);
            cmd.Parameters.AddWithValue("$endUtc", (object?)endIso ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", (int)dutyEvent.Status);

            // keep compatibility with your existing model (Location vs LocationText)
            var loc = (object?)dutyEvent.Location ?? (object?)dutyEvent.LocationText ?? DBNull.Value;
            cmd.Parameters.AddWithValue("$locText", loc);
            cmd.Parameters.AddWithValue("$notes", (object?)dutyEvent.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lat", (object?)dutyEvent.Lat ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lon", (object?)dutyEvent.Lon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$source", (object?)dutyEvent.Source ?? "manual");
            cmd.Parameters.AddWithValue("$isEdited", dutyEvent.IsEdited ? 1 : 0);
            cmd.Parameters.AddWithValue("$editedAt", (object?)dutyEvent.EditedAtUtc?.UtcDateTime.ToString("o") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$editReason", (object?)dutyEvent.EditReason ?? DBNull.Value);

            return (long)(cmd.ExecuteScalar() ?? 0L);
        }

        private static void UpdateEventRow(SqliteConnection conn, SqliteTransaction tx, long id, DutyEvent dutyEvent, string startIso, string? endIso)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                UPDATE duty_events
                SET start_utc = $start,
                    end_utc = $end,
                    status = $status,
                    location_text = $location,
                    notes = $notes,
                    lat = $lat,
                    lon = $lon,
                    source = $source,
                    is_edited = $isEdited,
                    edited_at_utc = $editedAt,
                    edit_reason = $editReason
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$start", startIso);
            cmd.Parameters.AddWithValue("$end", (object?)endIso ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", (int)dutyEvent.Status);

            var loc = (object?)dutyEvent.Location ?? (object?)dutyEvent.LocationText ?? DBNull.Value;
            cmd.Parameters.AddWithValue("$location", loc);
            cmd.Parameters.AddWithValue("$notes", (object?)dutyEvent.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lat", (object?)dutyEvent.Lat ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lon", (object?)dutyEvent.Lon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$source", (object?)dutyEvent.Source ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$isEdited", dutyEvent.IsEdited ? 1 : 0);
            cmd.Parameters.AddWithValue("$editedAt", (object?)dutyEvent.EditedAtUtc?.UtcDateTime.ToString("o") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$editReason", (object?)dutyEvent.EditReason ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        private static void NormalizeAround(SqliteConnection conn, SqliteTransaction tx, long anchorId)
        {
            // Remove zero-length rows
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText =
                    @"DELETE FROM duty_events
                      WHERE end_utc IS NOT NULL
                        AND end_utc <= start_utc;";
                del.ExecuteNonQuery();
            }

            // Pull rows in order (small DB, OK)
            var rows = new List<(long id, string startIso, string? endIso, int status)>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    @"SELECT id, start_utc, end_utc, status
                      FROM duty_events
                      ORDER BY start_utc ASC;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    rows.Add((
                        r.GetInt64(0),
                        r.GetString(1),
                        r.IsDBNull(2) ? null : r.GetString(2),
                        r.GetInt32(3)
                    ));
                }
            }

            if (rows.Count <= 1) return;

            // Ensure no overlaps: clamp each row end to next start
            for (int i = 0; i < rows.Count - 1; i++)
            {
                var (id, start, end, status) = rows[i];
                var nextStart = rows[i + 1].startIso;

                if (end == null || string.CompareOrdinal(end, nextStart) > 0)
                {
                    using var up = conn.CreateCommand();
                    up.Transaction = tx;
                    up.CommandText = @"UPDATE duty_events SET end_utc = $e WHERE id = $id;";
                    up.Parameters.AddWithValue("$id", id);
                    up.Parameters.AddWithValue("$e", nextStart);
                    up.ExecuteNonQuery();
                }
            }

            // Merge adjacent same-status segments when end == next start
            // (keep earlier row, delete later, extend end)
            rows.Clear();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    @"SELECT id, start_utc, end_utc, status
                      FROM duty_events
                      ORDER BY start_utc ASC;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    rows.Add((
                        r.GetInt64(0),
                        r.GetString(1),
                        r.IsDBNull(2) ? null : r.GetString(2),
                        r.GetInt32(3)
                    ));
                }
            }

            for (int i = 0; i < rows.Count - 1; i++)
            {
                var cur = rows[i];
                var nxt = rows[i + 1];

                if (cur.status == nxt.status && cur.endIso != null && string.Equals(cur.endIso, nxt.startIso, StringComparison.Ordinal))
                {
                    // Extend current end to next end (or null)
                    using (var up = conn.CreateCommand())
                    {
                        up.Transaction = tx;
                        up.CommandText = @"UPDATE duty_events SET end_utc = $e WHERE id = $id;";
                        up.Parameters.AddWithValue("$id", cur.id);
                        up.Parameters.AddWithValue("$e", (object?)nxt.endIso ?? DBNull.Value);
                        up.ExecuteNonQuery();
                    }

                    using (var del = conn.CreateCommand())
                    {
                        del.Transaction = tx;
                        del.CommandText = @"DELETE FROM duty_events WHERE id = $id;";
                        del.Parameters.AddWithValue("$id", nxt.id);
                        del.ExecuteNonQuery();
                    }

                    // restart scan after merge
                    rows = new List<(long id, string startIso, string? endIso, int status)>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                            @"SELECT id, start_utc, end_utc, status
                              FROM duty_events
                              ORDER BY start_utc ASC;";
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                        {
                            rows.Add((
                                r.GetInt64(0),
                                r.GetString(1),
                                r.IsDBNull(2) ? null : r.GetString(2),
                                r.GetInt32(3)
                            ));
                        }
                    }
                    i = -1; // will become 0
                }
            }

            // Final remove zero-length if any created
            using (var del2 = conn.CreateCommand())
            {
                del2.Transaction = tx;
                del2.CommandText =
                    @"DELETE FROM duty_events
                      WHERE end_utc IS NOT NULL
                        AND end_utc <= start_utc;";
                del2.ExecuteNonQuery();
            }
        }

        // =====================
        // DAILY LOG CERTIFICATION
        // =====================

        public static DailyLogCertification? GetLogCertification(DateTime localDate)
        {
            EnsureInit();

            var key = localDate.Date.ToString("yyyy-MM-dd");

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                @"SELECT id, log_date_local, signed_at_utc, driver_name, signature, certified, certification_text
                  FROM daily_log_certifications
                  WHERE log_date_local = $d
                  LIMIT 1;";
            cmd.Parameters.AddWithValue("$d", key);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new DailyLogCertification
            {
                Id = reader.GetInt64(0),
                LogDateLocal = reader.GetString(1),
                SignedAtUtc = DateTimeOffset.Parse(reader.GetString(2)).ToUniversalTime(),
                DriverName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Signature = reader.IsDBNull(4) ? null : reader.GetString(4),
                Certified = !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                CertificationText = reader.IsDBNull(6) ? null : reader.GetString(6)
            };
        }

        public static void UpsertLogCertification(DailyLogCertification cert)
        {
            EnsureInit();

            var key = (cert.LogDateLocal ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                key = DateTime.Today.ToString("yyyy-MM-dd");

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                @"INSERT INTO daily_log_certifications (log_date_local, signed_at_utc, driver_name, signature, certified, certification_text)
                  VALUES ($d, $s, $n, $sig, $c, $t)
                  ON CONFLICT(log_date_local) DO UPDATE SET
                    signed_at_utc = excluded.signed_at_utc,
                    driver_name   = excluded.driver_name,
                    signature     = excluded.signature,
                    certified     = excluded.certified,
                    certification_text = excluded.certification_text;";
            cmd.Parameters.AddWithValue("$d", key);
            cmd.Parameters.AddWithValue("$s", cert.SignedAtUtc.UtcDateTime.ToString("o"));
            cmd.Parameters.AddWithValue("$n", (object?)cert.DriverName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sig", (object?)cert.Signature ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$c", cert.Certified ? 1 : 0);
            cmd.Parameters.AddWithValue("$t", (object?)cert.CertificationText ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Returns all local dates within [startDate..endDate] that have at least one duty event overlapping that day.
        /// </summary>
        public static List<DateTime> GetLocalDatesWithDutyEvents(DateTime startDateLocal, DateTime endDateLocal)
        {
            EnsureInit();

            var dates = new List<DateTime>();
            var start = startDateLocal.Date;
            var end = endDateLocal.Date;
            if (end < start) (start, end) = (end, start);

            for (var day = start; day <= end; day = day.AddDays(1))
            {
                // Convert local day window to UTC
                var offset = TimeZoneInfo.Local.GetUtcOffset(day);
                var dayStartLocal = new DateTimeOffset(day, offset);
                var dayEndLocal = dayStartLocal.AddDays(1);
                var startUtc = dayStartLocal.ToUniversalTime();
                var endUtc = dayEndLocal.ToUniversalTime();

                if (HasDutyEventsInUtcWindow(startUtc, endUtc))
                    dates.Add(day);
            }

            return dates;
        }
        // ✅ NEW: include inspections when finding "days with activity" (used by Certify Logs)
        public static List<DateTime> GetLocalDatesWithAnyActivity(DateTime startDateLocal, DateTime endDateLocal)
        {
            EnsureInit();

            if (endDateLocal < startDateLocal)
            {
                var tmp = startDateLocal;
                startDateLocal = endDateLocal;
                endDateLocal = tmp;
            }

            var list = new List<DateTime>();

            // Loop local-day by local-day, but query in UTC windows
            for (var d = startDateLocal.Date; d <= endDateLocal.Date; d = d.AddDays(1))
            {
                var dayStartLocal = new DateTimeOffset(d, TimeZoneInfo.Local.GetUtcOffset(d));
                var dayEndLocal = dayStartLocal.AddDays(1);

                var startUtcIso = dayStartLocal.UtcDateTime.ToString("o");
                var endUtcIso = dayEndLocal.UtcDateTime.ToString("o");

                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();

                var hasDuty = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"SELECT COUNT(*)
                          FROM duty_events
                          WHERE start_utc >= $startUtc
                            AND start_utc <  $endUtc;";
                    cmd.Parameters.AddWithValue("$startUtc", startUtcIso);
                    cmd.Parameters.AddWithValue("$endUtc", endUtcIso);

                    var count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
                    hasDuty = count > 0;
                }

                var hasInspections = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"SELECT COUNT(*)
                          FROM inspections
                          WHERE created_utc >= $startUtc
                            AND created_utc <  $endUtc;";
                    cmd.Parameters.AddWithValue("$startUtc", startUtcIso);
                    cmd.Parameters.AddWithValue("$endUtc", endUtcIso);

                    var count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
                    hasInspections = count > 0;
                }

                if (hasDuty || hasInspections)
                    list.Add(d);
            }

            return list;
        }

        public sealed class InspectionSummary
        {
            public long Id { get; set; }
            public string Type { get; set; } = "";
            public string? VehicleId { get; set; }
            public string? Location { get; set; }
            public string? Notes { get; set; }
            public DateTimeOffset CreatedUtc { get; set; }
            public bool HasDefects { get; set; }
        }

        // ✅ NEW: list inspections within a UTC window (used by export)
        public static List<InspectionSummary> GetInspections(DateTimeOffset startUtc, DateTimeOffset endUtc)
        {
            EnsureInit();

            var list = new List<InspectionSummary>();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                @"SELECT i.id, i.type, i.vehicle_id, i.location, i.notes, i.created_utc,
                         (SELECT COUNT(*) FROM inspection_item_results r WHERE r.inspection_id = i.id AND r.status = 'defect') AS defect_count
                  FROM inspections i
                  WHERE i.created_utc >= $start
                    AND i.created_utc <  $end
                  ORDER BY i.created_utc ASC;";
            cmd.Parameters.AddWithValue("$start", startUtc.UtcDateTime.ToString("o"));
            cmd.Parameters.AddWithValue("$end", endUtc.UtcDateTime.ToString("o"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var createdIso = reader.IsDBNull(5) ? "" : reader.GetString(5);
                DateTimeOffset created = DateTimeOffset.UtcNow;
                try { created = DateTimeOffset.Parse(createdIso); } catch { }

                var defects = 0L;
                try { defects = reader.IsDBNull(6) ? 0 : reader.GetInt64(6); } catch { }

                list.Add(new InspectionSummary
                {
                    Id = reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
                    Type = reader.IsDBNull(1) ? "" : (reader.GetString(1) ?? ""),
                    VehicleId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Location = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Notes = reader.IsDBNull(4) ? null : reader.GetString(4),
                    CreatedUtc = created.ToUniversalTime(),
                    HasDefects = defects > 0
                });
            }

            return list;
        }

        public static List<DateTime> GetUnsignedLocalLogDates(DateTime startDateLocal, DateTime endDateLocal)
        {
            EnsureInit();

            var withEvents = GetLocalDatesWithDutyEvents(startDateLocal, endDateLocal);
            if (withEvents.Count == 0) return new List<DateTime>();

            var unsigned = new List<DateTime>();
            foreach (var day in withEvents)
            {
                if (GetLogCertification(day) == null)
                    unsigned.Add(day);
            }

            return unsigned.OrderByDescending(d => d).ToList();
        }

        private static bool HasDutyEventsInUtcWindow(DateTimeOffset startUtc, DateTimeOffset endUtc)
        {
            var startIso = startUtc.UtcDateTime.ToString("o");
            var endIso = endUtc.UtcDateTime.ToString("o");

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                @"SELECT COUNT(*)
                  FROM duty_events
                  WHERE start_utc < $end
                    AND (end_utc IS NULL OR end_utc > $start);";
            cmd.Parameters.AddWithValue("$start", startIso);
            cmd.Parameters.AddWithValue("$end", endIso);

            var count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            return count > 0;
        }

        // =====================
        // INSPECTIONS
        // =====================

        public static void DeleteAllInspections()
        {
            EnsureInit();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM inspections;";
            cmd.ExecuteNonQuery();
        }

        // Inspection insert (supports the UI named arguments)
        public static long InsertInspection(string type, string vehicleId, string notes, string signature, DateTimeOffset dateUtc)
        {
            EnsureInit();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO inspections (created_utc, type, vehicle_id, notes, signature)
                VALUES ($created, $type, $vehicle, $notes, $sig);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$created", dateUtc.UtcDateTime.ToString("o"));
            cmd.Parameters.AddWithValue("$type", type);
            cmd.Parameters.AddWithValue("$vehicle", vehicleId);
            cmd.Parameters.AddWithValue("$notes", notes);
            cmd.Parameters.AddWithValue("$sig", signature);

            return (long)cmd.ExecuteScalar()!;
        }

        public static void InsertInspectionItemResults(long inspectionId, IEnumerable<InspectionChecklistItem> items)
        {
            EnsureInit();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var tx = conn.BeginTransaction();

            // Clear any previous results for this inspection (safe to re-save)
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM inspection_item_results WHERE inspection_id = $iid;";
                del.Parameters.AddWithValue("$iid", inspectionId);
                del.ExecuteNonQuery();
            }

            foreach (var item in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    @"INSERT INTO inspection_item_results (inspection_id, item_key, item_name, category, status, note)
                      VALUES ($iid, $key, $name, $cat, $status, $note);";
                cmd.Parameters.AddWithValue("$iid", inspectionId);
                cmd.Parameters.AddWithValue("$key", item.Key);
                cmd.Parameters.AddWithValue("$name", item.Name);
                cmd.Parameters.AddWithValue("$cat", item.Category);
                cmd.Parameters.AddWithValue("$status", item.IsDefect ? "defect" : "ok");
                cmd.Parameters.AddWithValue("$note", string.IsNullOrWhiteSpace(item.Note) ? (object)DBNull.Value : item.Note);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public static (bool done, bool hasDefects) GetTodayInspectionStatus(string type)
        {
            EnsureInit();

            // Determine today's UTC window
            var nowUtc = DateTimeOffset.UtcNow;
            var dayStartUtc = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, TimeSpan.Zero);
            var dayEndUtc = dayStartUtc.AddDays(1);

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            long? inspectionId = null;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    @"SELECT id
                      FROM inspections
                      WHERE type = $type
                        AND created_utc >= $start
                        AND created_utc <  $end
                      ORDER BY created_utc DESC
                      LIMIT 1;";
                cmd.Parameters.AddWithValue("$type", type);
                cmd.Parameters.AddWithValue("$start", dayStartUtc.UtcDateTime.ToString("o"));
                cmd.Parameters.AddWithValue("$end", dayEndUtc.UtcDateTime.ToString("o"));

                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    inspectionId = Convert.ToInt64(result);
            }

            if (inspectionId is null)
                return (false, false);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    @"SELECT COUNT(*)
                      FROM inspection_item_results
                      WHERE inspection_id = $iid
                        AND status = 'defect';";
                cmd.Parameters.AddWithValue("$iid", inspectionId.Value);

                var count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
                return (true, count > 0);
            }
        }

        // ==========================
        // VTC Fleet + Mileage (Phase 2)
        // ==========================

        public sealed class VtcVehicleStatus
        {
            public string DiscordUserId { get; set; } = "";
            public string DriverName { get; set; } = "";
            public string? TruckMakeModel { get; set; }
            public double? OdometerMiles { get; set; }
            public double? FuelPct { get; set; }   // 0..1
            public double? DamagePct { get; set; } // 0..1
            public string? City { get; set; }
            public string? State { get; set; }
            public DateTimeOffset UpdatedUtc { get; set; }
        }

        public static void UpsertVtcVehicleStatus(VtcVehicleStatus s)
        {
            try
            {
                if (s == null) return;
                if (string.IsNullOrWhiteSpace(s.DiscordUserId)) return;
                if (string.IsNullOrWhiteSpace(s.DriverName)) s.DriverName = "Driver";
                EnsureInit();

                using var c = new SqliteConnection(ConnectionString);
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
INSERT INTO vtc_vehicle_status(discord_user_id, driver_name, truck_make_model, odometer_miles, fuel_pct, damage_pct, city, state, updated_utc)
VALUES($u,$n,$t,$o,$f,$d,$c,$s,$utc)
ON CONFLICT(discord_user_id) DO UPDATE SET
  driver_name=excluded.driver_name,
  truck_make_model=excluded.truck_make_model,
  odometer_miles=excluded.odometer_miles,
  fuel_pct=excluded.fuel_pct,
  damage_pct=excluded.damage_pct,
  city=excluded.city,
  state=excluded.state,
  updated_utc=excluded.updated_utc;";
                cmd.Parameters.AddWithValue("$u", s.DiscordUserId);
                cmd.Parameters.AddWithValue("$n", s.DriverName);
                cmd.Parameters.AddWithValue("$t", (object?)s.TruckMakeModel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$o", (object?)s.OdometerMiles ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$f", (object?)s.FuelPct ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$d", (object?)s.DamagePct ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$c", (object?)s.City ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$s", (object?)s.State ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$utc", (s.UpdatedUtc == default ? DateTimeOffset.UtcNow : s.UpdatedUtc).ToString("O"));
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static void AddMileageFromOdometer(string discordUserId, string driverName, double odometerMiles, DateTimeOffset utcNow)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(discordUserId)) return;
                if (string.IsNullOrWhiteSpace(driverName)) driverName = "Driver";
                if (odometerMiles <= 0) return;
                if (utcNow == default) utcNow = DateTimeOffset.UtcNow;

                EnsureInit();
                using var c = new SqliteConnection(ConnectionString);
                c.Open();

                // read previous
                double? prev = null;
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT last_odometer_miles FROM vtc_mileage_state WHERE discord_user_id=$u;";
                    cmd.Parameters.AddWithValue("$u", discordUserId);
                    using var r = cmd.ExecuteReader();
                    if (r.Read() && !r.IsDBNull(0)) prev = r.GetDouble(0);
                }

                // upsert state
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO vtc_mileage_state(discord_user_id, last_odometer_miles, updated_utc)
VALUES($u,$o,$utc)
ON CONFLICT(discord_user_id) DO UPDATE SET
  last_odometer_miles=excluded.last_odometer_miles,
  updated_utc=excluded.updated_utc;";
                    cmd.Parameters.AddWithValue("$u", discordUserId);
                    cmd.Parameters.AddWithValue("$o", odometerMiles);
                    cmd.Parameters.AddWithValue("$utc", utcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                }

                if (!prev.HasValue) return;

                var delta = odometerMiles - prev.Value;
                if (delta <= 0.01 || delta > 5000) return; // ignore resets/teleporting

                var dayLocal = DateTime.Now.ToString("yyyy-MM-dd"); // local day bucket

                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO vtc_mileage_daily(discord_user_id, driver_name, day_local, miles, updated_utc)
VALUES($u,$n,$day,$m,$utc)
ON CONFLICT(discord_user_id, day_local) DO UPDATE SET
  driver_name=excluded.driver_name,
  miles=miles + excluded.miles,
  updated_utc=excluded.updated_utc;";
                    cmd.Parameters.AddWithValue("$u", discordUserId);
                    cmd.Parameters.AddWithValue("$n", driverName);
                    cmd.Parameters.AddWithValue("$day", dayLocal);
                    cmd.Parameters.AddWithValue("$m", delta);
                    cmd.Parameters.AddWithValue("$utc", utcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        public static List<(string driverName, double miles)> GetMileageLeaderboard(int days)
        {
            var list = new List<(string, double)>();
            try
            {
                days = Math.Clamp(days, 1, 365);
                var from = DateTime.Now.Date.AddDays(-days + 1).ToString("yyyy-MM-dd");

                EnsureInit();
                using var c = new SqliteConnection(ConnectionString);
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
SELECT driver_name, SUM(miles) as total
FROM vtc_mileage_daily
WHERE day_local >= $from
GROUP BY driver_name
ORDER BY total DESC
LIMIT 50;";
                cmd.Parameters.AddWithValue("$from", from);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var name = r.IsDBNull(0) ? "Driver" : r.GetString(0);
                    var miles = r.IsDBNull(1) ? 0.0 : r.GetDouble(1);
                    list.Add((name, miles));
                }
            }
            catch { }
            return list;
        }

        public static List<VtcVehicleStatus> GetFleetStatus()
        {
            var list = new List<VtcVehicleStatus>();
            try
            {
                EnsureInit();
                using var c = new SqliteConnection(ConnectionString);
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
SELECT discord_user_id, driver_name, truck_make_model, odometer_miles, fuel_pct, damage_pct, city, state, updated_utc
FROM vtc_vehicle_status
ORDER BY driver_name;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var s = new VtcVehicleStatus
                    {
                        DiscordUserId = r.IsDBNull(0) ? "" : r.GetString(0),
                        DriverName = r.IsDBNull(1) ? "Driver" : r.GetString(1),
                        TruckMakeModel = r.IsDBNull(2) ? null : r.GetString(2),
                        OdometerMiles = r.IsDBNull(3) ? null : r.GetDouble(3),
                        FuelPct = r.IsDBNull(4) ? null : r.GetDouble(4),
                        DamagePct = r.IsDBNull(5) ? null : r.GetDouble(5),
                        City = r.IsDBNull(6) ? null : r.GetString(6),
                        State = r.IsDBNull(7) ? null : r.GetString(7),
                        UpdatedUtc = r.IsDBNull(8) ? DateTimeOffset.MinValue : DateTimeOffset.Parse(r.GetString(8))
                    };
                    list.Add(s);
                }
            }
            catch { }
            return list;
        }

    }
}