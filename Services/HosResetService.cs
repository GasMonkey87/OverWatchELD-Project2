using System;
using System.IO;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class HosResetService
    {
        public sealed class ResetResult
        {
            public bool Ok { get; init; }
            public string Message { get; init; } = "";
            public string? DbPath { get; init; }
            public string? DutyTable { get; init; }
        }

        public static ResetResult ResetClocksNow()
        {
            try
            {
                DatabaseService.Initialize();

                var nowUtc = EldClock.UtcNow;
                var anchorUtc = nowUtc.AddHours(-34);

                // Clear any existing duty history.
                DatabaseService.DeleteAllDutyEvents();

                // Insert one open OFF-DUTY event that started 34 hours ago.
                // HosCalculator2 treats the end of this qualifying off-duty stretch as "now",
                // which restores the 11/14/70 clocks to full values.
                DatabaseService.InsertDutyEvent(new DutyEvent
                {
                    Status = DutyStatus.OffDuty,
                    StartUtc = anchorUtc,
                    EndUtc = null,
                    Notes = "Manual 34-hour reset",
                    Source = "manual-reset",
                    LocationText = "",
                    Lat = null,
                    Lon = null,
                    IsEdited = false,
                    EditedAtUtc = null,
                    EditReason = ""
                });

                // Clear display-only override so dashboard uses real DB-backed clocks.
                HosCalculator.ClearManualReset();

                return new ResetResult
                {
                    Ok = true,
                    Message = "Clocks reset successfully.",
                    DbPath = GuessDbPath(),
                    DutyTable = "duty_events"
                };
            }
            catch (Exception ex)
            {
                return new ResetResult
                {
                    Ok = false,
                    Message = "Reset failed: " + ex.Message,
                    DbPath = GuessDbPath(),
                    DutyTable = "duty_events"
                };
            }
        }

        private static string? GuessDbPath()
        {
            try
            {
                var p = AppPaths.FileInData("OverWatchELD.db");
                if (File.Exists(p)) return p;
            }
            catch { }

            try
            {
                var p = Path.Combine(AppContext.BaseDirectory, "Data", "OverWatchELD.db");
                if (File.Exists(p)) return p;
            }
            catch { }

            return null;
        }
    }
}
