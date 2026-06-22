using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OverWatchELD.Models;

namespace OverWatchELD.Views
{
    public partial class EditDutyEventWindow : Window
    {
        public DutyEvent? Result { get; private set; }
        private readonly DutyEvent? _editing;

        // Base local dates (since your window has no date pickers)
        private readonly DateTime _startDateLocal;
        private readonly DateTime _endDateLocal;

        public EditDutyEventWindow(DutyEvent? editing = null)
        {
            InitializeComponent();
            _editing = editing;

            // Use existing event dates; otherwise today
            if (_editing != null)
            {
                var s = _editing.StartUtc.ToLocalTime().DateTime;
                var e = (_editing.EndUtc ?? DateTimeOffset.UtcNow).ToLocalTime().DateTime;
                _startDateLocal = s.Date;
                _endDateLocal = e.Date;
            }
            else
            {
                _startDateLocal = DateTime.Today;
                _endDateLocal = DateTime.Today;
            }

            FillTimeCombos();
            HookKeyboardFineAdjust();
            LoadDefaults();
        }

        private void FillTimeCombos()
        {
            StartHour.Items.Clear(); EndHour.Items.Clear();
            StartMin.Items.Clear(); EndMin.Items.Clear();
            StartSec.Items.Clear(); EndSec.Items.Clear();

            for (int h = 0; h < 24; h++)
            {
                var v = h.ToString("00");
                StartHour.Items.Add(v);
                EndHour.Items.Add(v);
            }

            for (int m = 0; m < 60; m++)
            {
                var v = m.ToString("00");
                StartMin.Items.Add(v);
                EndMin.Items.Add(v);
            }

            // ✅ 1-second increments (00–59)
            for (int s = 0; s < 60; s++)
            {
                var v = s.ToString("00");
                StartSec.Items.Add(v);
                EndSec.Items.Add(v);
            }

            // Defaults
            StartHour.SelectedIndex = 0;
            StartMin.SelectedIndex = 0;
            StartSec.SelectedIndex = 0;

            EndHour.SelectedIndex = 0;
            EndMin.SelectedIndex = 0;
            EndSec.SelectedIndex = 0;
        }

        private void LoadDefaults()
        {
            if (_editing != null)
            {
                SelectStatus(NormalizeStatus(_editing.Status.ToString()));

                var s = _editing.StartUtc.ToLocalTime().DateTime;
                var e = (_editing.EndUtc ?? DateTimeOffset.UtcNow).ToLocalTime().DateTime;

                StartHour.SelectedItem = s.Hour.ToString("00");
                StartMin.SelectedItem = s.Minute.ToString("00");
                StartSec.SelectedItem = s.Second.ToString("00");

                EndHour.SelectedItem = e.Hour.ToString("00");
                EndMin.SelectedItem = e.Minute.ToString("00");
                EndSec.SelectedItem = e.Second.ToString("00");

                LocationBox.Text = _editing.LocationText ?? _editing.Location ?? "";
                NotesBox.Text = _editing.Notes ?? "";
            }
            else
            {
                SelectStatus("OFF");

                var now = DateTime.Now;

                StartHour.SelectedItem = now.Hour.ToString("00");
                StartMin.SelectedItem = now.Minute.ToString("00");
                StartSec.SelectedItem = now.Second.ToString("00");

                var end = now.AddMinutes(15);
                EndHour.SelectedItem = end.Hour.ToString("00");
                EndMin.SelectedItem = end.Minute.ToString("00");
                EndSec.SelectedItem = end.Second.ToString("00");
            }
        }

        private void HookKeyboardFineAdjust()
        {
            // ✅ ↑/↓ = ±1 (Shift = ±10) on seconds and minutes
            StartSec.PreviewKeyDown += (_, e) => HandleStepKeys(e, StartSec, 0, 59);
            EndSec.PreviewKeyDown += (_, e) => HandleStepKeys(e, EndSec, 0, 59);

            StartMin.PreviewKeyDown += (_, e) => HandleStepKeys(e, StartMin, 0, 59);
            EndMin.PreviewKeyDown += (_, e) => HandleStepKeys(e, EndMin, 0, 59);
        }

        private static void HandleStepKeys(KeyEventArgs e, ComboBox box, int min, int max)
        {
            if (e.Key != Key.Up && e.Key != Key.Down) return;

            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            if (e.Key == Key.Down) step = -step;

            int cur = ParseBoxInt(box, min);
            int next = cur + step;

            if (next < min) next = min;
            if (next > max) next = max;

            box.SelectedItem = next.ToString("00", CultureInfo.InvariantCulture);
            e.Handled = true;
        }

        private static int ParseBoxInt(ComboBox box, int fallback)
        {
            var s = box.SelectedItem?.ToString();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;
            return fallback;
        }

        private void SelectStatus(string label)
        {
            label = label.ToUpperInvariant();
            for (int i = 0; i < StatusBox.Items.Count; i++)
            {
                if (StatusBox.Items[i] is ComboBoxItem cbi &&
                    string.Equals((cbi.Content?.ToString() ?? ""), label, StringComparison.OrdinalIgnoreCase))
                {
                    StatusBox.SelectedIndex = i;
                    return;
                }
            }
            StatusBox.SelectedIndex = 0;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var statusLabel = (StatusBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "OFF";
            statusLabel = NormalizeStatus(statusLabel);

            if (!TryMapDutyStatus(statusLabel, out var status))
            {
                MessageBox.Show("Status must be OFF, SB, DRIVE, ON, PC, or YM.", "Invalid",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Since your UI has no date pickers: keep date from the event (or today)
            var sLocal = CombineLocal(_startDateLocal, StartHour, StartMin, StartSec);
            var eLocal = CombineLocal(_endDateLocal, EndHour, EndMin, EndSec);

            // If end is earlier than start on same day, assume it crosses midnight into next day
            if (eLocal <= sLocal)
                eLocal = eLocal.AddDays(1);

            if (eLocal <= sLocal)
            {
                MessageBox.Show("End time must be after start time.", "Invalid",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var model = _editing ?? new DutyEvent();
            model.Status = status;
            model.StartUtc = sLocal.ToUniversalTime();
            model.EndUtc = eLocal.ToUniversalTime();
            model.LocationText = LocationBox.Text?.Trim();
            model.Notes = NotesBox.Text?.Trim();
            model.Source = string.IsNullOrWhiteSpace(model.Source) ? "manual" : model.Source;

            Result = model;
            DialogResult = true;
            Close();
        }

        private static DateTimeOffset CombineLocal(DateTime date, ComboBox hourBox, ComboBox minBox, ComboBox secBox)
        {
            var hh = int.Parse((hourBox.SelectedItem?.ToString() ?? "00"), CultureInfo.InvariantCulture);
            var mm = int.Parse((minBox.SelectedItem?.ToString() ?? "00"), CultureInfo.InvariantCulture);
            var ss = int.Parse((secBox.SelectedItem?.ToString() ?? "00"), CultureInfo.InvariantCulture);

            var dt = new DateTime(date.Year, date.Month, date.Day, hh, mm, ss, DateTimeKind.Unspecified);
            var off = TimeZoneInfo.Local.GetUtcOffset(dt);
            return new DateTimeOffset(dt, off);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static string NormalizeStatus(string raw)
        {
            var s = (raw ?? "").Trim().ToUpperInvariant();
            if (s.StartsWith("OFF")) return "OFF";
            if (s == "SB" || s.Contains("SLEEP")) return "SB";
            if (s == "D" || s == "DRIVE" || s.Contains("DRIV")) return "DRIVE";
            if (s.StartsWith("ON")) return "ON";
            if (s == "PC") return "PC";
            if (s == "YM") return "YM";
            return s;
        }

        private static bool TryMapDutyStatus(string normalized, out DutyStatus status)
        {
            var s = NormalizeStatus(normalized);

            if (Enum.TryParse<DutyStatus>(s, true, out status)) return true;

            string[] candidates = s switch
            {
                "OFF" => new[] { "Off", "OFF", "OffDuty", "OFF_DUTY" },
                "SB" => new[] { "SB", "Sleeper", "SleeperBerth" },
                "DRIVE" => new[] { "D", "Driving", "Drive", "DRIVE" },
                "ON" => new[] { "ON", "OnDuty", "ON_DUTY" },
                "PC" => new[] { "PC", "PersonalConveyance" },
                "YM" => new[] { "YM", "YardMove" },
                _ => Array.Empty<string>()
            };

            foreach (var c in candidates)
                if (Enum.TryParse<DutyStatus>(c, true, out status)) return true;

            status = default;
            return false;
        }
    }
}
