using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Services
{
    public static class DotInspectionExportService
    {
        /// <summary>
        /// Exports the DOT day table to a CSV file in Documents\ATS_ELD\DOT_Inspection_YYYYMMDD_HHMM.csv
        /// Returns the full file path.
        /// </summary>
        public static string ExportDaysToCsv(IEnumerable<DotDaySummary> days)
        {
            var list = days?.ToList() ?? new List<DotDaySummary>();

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ATS_ELD");
            Directory.CreateDirectory(dir);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
            var path = Path.Combine(dir, $"DOT_Inspection_{stamp}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Day,OFF,SB,DRIVE,ON,TOTAL,Violations");

            foreach (var d in list)
            {
                sb.AppendLine($"{Csv(d.DayLabel)},{Csv(d.OffText)},{Csv(d.SbText)},{Csv(d.DriveText)},{Csv(d.OnText)},{Csv(d.OnDutyTotalText)},{Csv(d.ViolationText)}");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private static string Csv(string? s)
        {
            s ??= "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
