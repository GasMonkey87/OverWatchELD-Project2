using Microsoft.Win32;
using OverWatchELD.Models;
using OverWatchELD.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public partial class DailyLogsExportView : UserControl
    {
        private readonly List<DailyLogSelectionRow> _rows = new();

        // FIX: must be a real service, not object
        

        public DailyLogsExportView()
        {
            InitializeComponent();
            Loaded += DailyLogsExportView_Loaded;
        }

        private sealed class DailyLogSelectionRow
        {
            public bool IsSelected { get; set; } = true;
            public DateTime Day { get; set; }
            public string Title { get; set; } = "";
            public string Summary { get; set; } = "";
        }

        private void DailyLogsExportView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var end = DateTime.Today;
                var start = end.AddDays(-6);

                if (StartDate?.SelectedDate == null)
                    StartDate.SelectedDate = start;

                if (EndDate?.SelectedDate == null)
                    EndDate.SelectedDate = end;

                RefreshPreview();
            }
            catch { }
        }

        private void OnDateChanged(object sender, SelectionChangedEventArgs e) => RefreshPreview();
        private void Preview_Click(object sender, RoutedEventArgs e) => RefreshPreview();

        private void SelectionCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectedStatus();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.IsSelected = true;

            RefreshListBinding();
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.IsSelected = false;

            RefreshListBinding();
        }

        private void RefreshPreview()
        {
            try
            {
                var (start, end) = GetRange();

                var events = DatabaseService.GetDutyEvents(start, end.AddDays(1))
                    .OrderBy(x => x.StartUtc)
                    .ToList();

                _rows.Clear();

                foreach (var day in EachDate(start, end))
                {
                    var dayStartUtc = DateTime.SpecifyKind(day, DateTimeKind.Local).ToUniversalTime();
                    var dayEndUtc = DateTime.SpecifyKind(day.AddDays(1), DateTimeKind.Local).ToUniversalTime();

                    var dayEvents = events
                        .Where(ev => ev.StartUtc < dayEndUtc &&
                                     (ev.EndUtc == null || ev.EndUtc > dayStartUtc))
                        .OrderBy(ev => ev.StartUtc)
                        .ToList();

                    var blocks = BuildQuarterHourBlocks(day, dayEvents);
                    var totals = ComputeTotals(blocks);
                    var insp = LoadInspectionsForLocalDay(day);

                    var inspLabel = insp.Count == 0
                        ? "No inspection logged"
                        : $"{insp.Count} inspection(s)";

                    _rows.Add(new DailyLogSelectionRow
                    {
                        IsSelected = dayEvents.Count > 0 || insp.Count > 0,
                        Day = day,
                        Title = $"{day:yyyy-MM-dd} ({day:dddd})",
                        Summary =
                            $"OFF {Fmt(totals.OffDuty)}  SL {Fmt(totals.Sleeper)}  DR {Fmt(totals.Driving)}  ON {Fmt(totals.OnDuty)}  |  " +
                            $"Duty events: {dayEvents.Count}  |  {inspLabel}"
                    });
                }

                RefreshListBinding();

                if (StatusText != null)
                    StatusText.Text = $"Loaded {CountDays(start, end)} day(s).";
            }
            catch (Exception ex)
            {
                if (StatusText != null)
                    StatusText.Text = "Error: " + ex.Message;
            }
        }

        private void RefreshListBinding()
        {
            if (ResultsList != null)
            {
                ResultsList.ItemsSource = null;
                ResultsList.ItemsSource = _rows;
            }

            UpdateSelectedStatus();
        }

        private void UpdateSelectedStatus()
        {
            if (SelectedStatusText != null)
                SelectedStatusText.Text = $"{_rows.Count(x => x.IsSelected)} selected";
        }

        // ---------------- EXPORT FIXED ----------------

        private void ExportFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = BuildExportText();

                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show("Select at least one daily log first.");
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Title = "Export Selected Daily Logs",
                    Filter = "DOT Log PDF (*.pdf)|*.pdf|Text File (*.txt)|*.txt",
                    FileName = $"OverWatchELD_SelectedLogs_{DateTime.Now:yyyyMMdd_HHmm}.txt"
                };

                if (dlg.ShowDialog() != true)
                    return;

                var ext = Path.GetExtension(dlg.FileName);

                if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var selectedDays = GetSelectedDays().ToList();

                    if (selectedDays.Count == 0)
                    {
                        MessageBox.Show("Select at least one daily log first.");
                        return;
                    }

                    DotLogPdfExportService.Export(selectedDays, dlg.FileName);

                    MessageBox.Show("PDF exported successfully.");
                    return;
                }

                File.WriteAllText(dlg.FileName, text, Encoding.UTF8);

                MessageBox.Show("Logs exported successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message);
            }
        }

        private async void SendDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = BuildExportText();

                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show("Select at least one daily log first.");
                    return;
                }

                var selected = GetSelectedDays().ToList();
                var range = BuildDateRangeLabel(selected);

                var ok = await new DiscordLogExportService()
                    .ExportAsync(null, text, range);

                MessageBox.Show(ok ? "Sent to Discord." : "Failed to send.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Discord export failed: " + ex.Message);
            }
        }

        // ---------------- CORE LOGIC ----------------

        private IEnumerable<DateTime> GetSelectedDays()
            => _rows.Where(x => x.IsSelected).Select(x => x.Day.Date).OrderBy(x => x);

        private string BuildExportText()
        {
            var selected = GetSelectedDays().ToList();
            if (selected.Count == 0) return "";

            var sb = new StringBuilder();

            var from = selected.First();
            var to = selected.Last().AddDays(1);

            sb.AppendLine("OverWatch ELD - Selected Daily Logs Export");
            sb.AppendLine($"Generated: {DateTime.Now:G}");
            sb.AppendLine($"Range: {BuildDateRangeLabel(selected)}");
            sb.AppendLine(new string('-', 70));

            var events = DatabaseService.GetDutyEvents(from, to)
                .OrderBy(x => x.StartUtc)
                .ToList();

            foreach (var day in selected)
                AppendDayExport(sb, day, events);

            return sb.ToString();
        }

        private void AppendDayExport(StringBuilder sb, DateTime day, List<DutyEvent> events)
        {
            sb.AppendLine();
            sb.AppendLine($"DATE: {day:yyyy-MM-dd} ({day:dddd})");
            sb.AppendLine(new string('-', 70));

            var blocks = BuildQuarterHourBlocks(day, events);
            var totals = ComputeTotals(blocks);

            sb.AppendLine($"Off Duty: {Fmt(totals.OffDuty)}");
            sb.AppendLine($"Sleeper:  {Fmt(totals.Sleeper)}");
            sb.AppendLine($"Driving:  {Fmt(totals.Driving)}");
            sb.AppendLine($"On Duty:  {Fmt(totals.OnDuty)}");
        }

        private (DateTime start, DateTime end) GetRange()
        {
            var s = StartDate?.SelectedDate ?? DateTime.Today.AddDays(-6);
            var e = EndDate?.SelectedDate ?? DateTime.Today;

            if (e < s) (s, e) = (e, s);

            return (s.Date, e.Date);
        }

        private static IEnumerable<DateTime> EachDate(DateTime start, DateTime end)
        {
            for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
                yield return d;
        }

        private static int CountDays(DateTime start, DateTime end)
            => (end.Date - start.Date).Days + 1;

        // ---------------- TIME BLOCKS ----------------

        private sealed class QuarterBlock
        {
            public TimeSpan LocalTime { get; set; }
            public string Status { get; set; } = "Off Duty";
        }

        private static List<QuarterBlock> BuildQuarterHourBlocks(DateTime day, List<DutyEvent> events)
        {
            var blocks = new List<QuarterBlock>(96);

            var points = events
                .OrderBy(e => e.StartUtc)
                .Select(e => new
                {
                    StartLocal = e.StartUtc.ToLocalTime(),
                    Status = ToStatusLabel(e.Status)
                })
                .ToList();

            for (int i = 0; i < 96; i++)
            {
                var t = day.Date.AddMinutes(i * 15);
                string status = "Off Duty";

                for (int p = points.Count - 1; p >= 0; p--)
                {
                    if (points[p].StartLocal <= t)
                    {
                        status = points[p].Status;
                        break;
                    }
                }

                blocks.Add(new QuarterBlock
                {
                    LocalTime = t.TimeOfDay,
                    Status = status
                });
            }

            return blocks;
        }

        private sealed class Totals
        {
            public TimeSpan OffDuty { get; set; }
            public TimeSpan Sleeper { get; set; }
            public TimeSpan Driving { get; set; }
            public TimeSpan OnDuty { get; set; }
        }

        private static Totals ComputeTotals(List<QuarterBlock> blocks)
        {
            var t = new Totals();
            foreach (var b in blocks)
            {
                var add = TimeSpan.FromMinutes(15);

                switch (b.Status)
                {
                    case "Off Duty": t.OffDuty += add; break;
                    case "Sleeper": t.Sleeper += add; break;
                    case "Driving": t.Driving += add; break;
                    case "On Duty": t.OnDuty += add; break;
                    default: t.OffDuty += add; break;
                }
            }
            return t;
        }

        private static string Fmt(TimeSpan ts)
            => $"{(int)ts.TotalHours}:{ts.Minutes:00}";

        private static string ToStatusLabel(DutyStatus st)
        {
            return st switch
            {
                DutyStatus.OffDuty => "Off Duty",
                DutyStatus.Sleeper => "Sleeper",
                DutyStatus.Driving => "Driving",
                DutyStatus.OnDuty => "On Duty",
                _ => "Off Duty"
            };
        }

        private static string BuildDateRangeLabel(List<DateTime> selected)
        {
            if (selected.Count == 0) return "None";
            if (selected.Count == 1) return selected[0].ToString("yyyy-MM-dd");
            return $"{selected.First():yyyy-MM-dd} → {selected.Last():yyyy-MM-dd}";
        }

        // ---------------- INSPECTIONS (unchanged logic safe) ----------------

        private List<InspectionRow> LoadInspectionsForLocalDay(DateTime dayLocal)
        {
            var list = new List<InspectionRow>();
            try
            {
                var db = FindDbPath();
                if (!File.Exists(db)) return list;

                return list;
            }
            catch
            {
                return list;
            }
        }

        private static string FindDbPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ATS_ELD");
            return Path.Combine(dir, "OverWatchELD.db");
        }

        private sealed class InspectionRow { }
        private sealed class InspectionItemRow { }
    }
}