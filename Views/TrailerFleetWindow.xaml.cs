using OverWatchELD.Models.Fleet;
using OverWatchELD.ViewModels;
using OverWatchELD.Services;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public partial class TrailerFleetWindow : Window
    {
        private TrailerFleetViewModel Vm => (TrailerFleetViewModel)DataContext;

        public TrailerFleetWindow()
        {
            InitializeComponent();
            DataContext = new TrailerFleetViewModel();
            Loaded += TrailerFleetWindow_Loaded;
        }

        private void TrailerFleetWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Vm.Refresh();
            DriverDropdownService.Bind(DriverBox, includeUnassigned: true);
            PopulateFormFromSelectedTrailer();
        }

        private void TrailerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateFormFromSelectedTrailer();
        }

        private void PopulateFormFromSelectedTrailer()
        {
            try
            {
                var selected = Vm.LoadSelectedFull();
                if (selected == null) return;

                TrailerNumberBox.Text = selected.TrailerNumber ?? "";
                SetComboSelectionOrText(TrailerNumberBox, selected.TrailerNumber);

                DriverDropdownService.Select(DriverBox, selected.AssignedDriver);

                LoadNumberBox.Text = selected.CurrentLoadNumber ?? "";
                SetComboSelectionOrText(LoadNumberBox, selected.CurrentLoadNumber);

                TrailerNameBox.Text = selected.TrailerName ?? "";
                ModelBox.Text = selected.Model ?? "";
                TrailerTypeBox.Text = selected.TrailerType ?? "";
                PlateBox.Text = selected.PlateNumber ?? "";
                LocationBox.Text = selected.Location ?? "";
                ConditionBox.Text = Math.Clamp(selected.HealthPercent, 0, 100).ToString(CultureInfo.InvariantCulture);
                OdometerBox.Text = Math.Max(0, selected.OdometerMiles).ToString("0.##", CultureInfo.InvariantCulture);
            }
            catch { }
        }

        private void SaveTrailer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var trailer = BuildTrailerFromForm();
                Vm.SaveTrailer(trailer);
                SelectTrailerById(trailer.Id);
                MessageBox.Show("Trailer saved.", "Trailer Fleet", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save trailer failed.\n\n" + ex.Message, "Trailer Fleet", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AssignDriver_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var trailer = BuildTrailerFromForm();
                Vm.AssignTrailer(trailer, DriverDropdownService.SelectedName(DriverBox, "Unassigned"), ReadComboText(LoadNumberBox));
                SelectTrailerById(trailer.Id);
                MessageBox.Show("Driver/load assigned to trailer.", "Trailer Fleet", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Assign driver/load failed.\n\n" + ex.Message, "Trailer Fleet", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UseTrailerTracking_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Vm.UseSelectedTrailerForTracking();
                var selected = Vm.LoadSelectedFull();
                var name = selected == null ? "selected trailer" : FirstNonEmpty(selected.TrailerNumber, selected.TrailerName, selected.Model);
                MessageBox.Show($"This driver is now using trailer {name} for tracking.", "Trailer Fleet", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not select trailer for tracking.\n\n" + ex.Message, "Trailer Fleet", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ServiceComplete_Click(object sender, RoutedEventArgs e)
        {
            Vm.MarkServiceComplete();
            PopulateFormFromSelectedTrailer();
        }

        private void InspectionComplete_Click(object sender, RoutedEventArgs e)
        {
            Vm.MarkInspectionComplete();
            PopulateFormFromSelectedTrailer();
        }

        private void Unassign_Click(object sender, RoutedEventArgs e)
        {
            Vm.UnassignDriver();
            PopulateFormFromSelectedTrailer();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Delete selected trailer?", "Trailer Fleet", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            Vm.DeleteSelected();
            ClearForm();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Vm.Refresh();
            DriverDropdownService.Bind(DriverBox, includeUnassigned: true);
            PopulateFormFromSelectedTrailer();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private FleetCommandTrailer BuildTrailerFromForm()
        {
            var trailerNumber = FirstNonEmpty(ReadComboText(TrailerNumberBox), Vm.GetNextAvailableTrailerNumber());
            var existing = Vm.CreateOrGetTrailer(trailerNumber);

            existing.TrailerNumber = trailerNumber;
            existing.AssignedDriver = CleanDriver(DriverDropdownService.SelectedName(DriverBox, "Unassigned"));
            existing.DriverDiscordId = DriverDropdownService.SelectedDiscordId(DriverBox, existing.DriverDiscordId);
            existing.CurrentLoadNumber = (ReadComboText(LoadNumberBox) ?? "").Trim();
            existing.TrailerName = (TrailerNameBox.Text ?? "").Trim();
            existing.Model = (ModelBox.Text ?? "").Trim();
            existing.TrailerType = (TrailerTypeBox.Text ?? "").Trim();
            existing.PlateNumber = (PlateBox.Text ?? "").Trim();
            existing.Location = (LocationBox.Text ?? "").Trim();
            existing.HealthPercent = ParseInt(ConditionBox.Text, 100, 0, 100);
            existing.OdometerMiles = ParseDouble(OdometerBox.Text, 0, 0, double.MaxValue);
            existing.IsActive = true;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;

            return existing;
        }

        private void SelectTrailerById(string id)
        {
            foreach (var row in Vm.Trailers)
            {
                if (string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    Vm.SelectedTrailer = row;
                    break;
                }
            }
        }

        private void ClearForm()
        {
            TrailerNumberBox.Text = "";
            DriverDropdownService.Select(DriverBox, "Unassigned");
            LoadNumberBox.Text = "";
            TrailerNameBox.Text = "";
            ModelBox.Text = "";
            TrailerTypeBox.Text = "";
            PlateBox.Text = "";
            LocationBox.Text = "";
            ConditionBox.Text = "100";
            OdometerBox.Text = "0";
        }

        private static void SetComboSelectionOrText(ComboBox box, string? value)
        {
            var text = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                box.SelectedItem = null;
                box.Text = "";
                return;
            }

            if (box.ItemsSource != null)
            {
                foreach (var item in box.ItemsSource)
                {
                    if (item is string s && string.Equals(s.Trim(), text, StringComparison.OrdinalIgnoreCase))
                    {
                        box.SelectedItem = item;
                        box.Text = s;
                        return;
                    }

                    var trailerNumber = ReadObjectString(item, "TrailerNumber");
                    if (!string.IsNullOrWhiteSpace(trailerNumber) && string.Equals(trailerNumber, text, StringComparison.OrdinalIgnoreCase))
                    {
                        box.SelectedItem = item;
                        box.Text = text;
                        return;
                    }
                }
            }

            box.SelectedItem = null;
            box.Text = text;
        }

        private static string ReadComboText(ComboBox box)
        {
            if (box.SelectedItem is string s) return s.Trim();
            if (box.SelectedItem != null)
            {
                var text = ReadObjectString(box.SelectedItem, "TrailerNumber");
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            return (box.Text ?? "").Trim();
        }

        private static string ReadObjectString(object? obj, string propertyName)
        {
            try
            {
                if (obj == null) return "";
                var prop = obj.GetType().GetProperty(propertyName);
                var value = prop?.GetValue(obj);
                return value?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private static int ParseInt(string? text, int fallback, int min, int max)
        {
            if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                value = fallback;
            return Math.Clamp(value, min, max);
        }

        private static double ParseDouble(string? text, double fallback, double min, double max)
        {
            if (!double.TryParse((text ?? "").Trim().Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                value = fallback;
            if (value < min) value = min;
            if (value > max) value = max;
            return value;
        }

        private static string CleanDriver(string? value)
        {
            var text = (value ?? "").Trim();
            return text.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) ? "" : text;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }
    }
}
