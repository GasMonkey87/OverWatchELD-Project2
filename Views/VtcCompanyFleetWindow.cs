using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OverWatchELD.Models.Fleet;
using OverWatchELD.Services.Fleet;

namespace OverWatchELD.Views
{
    public sealed class VtcCompanyFleetWindow : Window
    {
        private readonly DataGrid _grid;
        private readonly TextBlock _status;

        public VtcCompanyFleetWindow()
        {
            Title = "Company Fleet";
            Width = 1180;
            Height = 760;
            MinWidth = 900;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.Black;

            var root = new DockPanel { Margin = new Thickness(14) };

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock
            {
                Text = "Company Fleet",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold
            });
            header.Children.Add(new TextBlock
            {
                Text = "Truck numbers and basic fleet information. Double-click a truck to open its profile.",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 4, 0, 0)
            });
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            _status = new TextBlock { Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center };
            var refresh = new Button { Content = "Refresh", Width = 110, Height = 34, HorizontalAlignment = HorizontalAlignment.Right };
            refresh.Click += (_, __) => LoadFleet();
            DockPanel.SetDock(refresh, Dock.Right);
            bottom.Children.Add(refresh);
            bottom.Children.Add(_status);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                RowHeaderWidth = 0,
                Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
                RowBackground = new SolidColorBrush(Color.FromRgb(20, 20, 20))
            };

            _grid.Columns.Add(new DataGridTextColumn { Header = "Truck #", Binding = new System.Windows.Data.Binding(nameof(FleetRow.TruckNumber)), Width = new DataGridLength(1.0, DataGridLengthUnitType.Star) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Make / Model", Binding = new System.Windows.Data.Binding(nameof(FleetRow.MakeModel)), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Assigned Driver", Binding = new System.Windows.Data.Binding(nameof(FleetRow.AssignedDriver)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Mileage", Binding = new System.Windows.Data.Binding(nameof(FleetRow.MileageDisplay)), Width = new DataGridLength(1.0, DataGridLengthUnitType.Star) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Fuel", Binding = new System.Windows.Data.Binding(nameof(FleetRow.FuelDisplay)), Width = new DataGridLength(0.8, DataGridLengthUnitType.Star) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Condition", Binding = new System.Windows.Data.Binding(nameof(FleetRow.ConditionDisplay)), Width = new DataGridLength(0.9, DataGridLengthUnitType.Star) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new System.Windows.Data.Binding(nameof(FleetRow.Status)), Width = new DataGridLength(1.0, DataGridLengthUnitType.Star) });
            _grid.MouseDoubleClick += Grid_MouseDoubleClick;

            root.Children.Add(_grid);
            Content = root;
            Loaded += (_, __) => LoadFleet();
        }

        private void LoadFleet()
        {
            try
            {
                var rows = new List<FleetRow>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // The VTC Admin / Fleet Command Center saves trucks into FleetCommandStore
                // (%AppData%\OverWatchELD\fleet_unified.json). Load that first so the
                // public VTC Fleet page shows the same company fleet admins manage.
                foreach (var truck in new FleetCommandStore().LoadAll())
                {
                    var row = FleetRow.FromCommandTruck(truck);
                    if (seen.Add(row.IdentityKey))
                        rows.Add(row);
                }

                // Also include older maintenance/repository trucks that may not have been
                // migrated yet. This keeps legacy fleets visible instead of showing blank.
                foreach (var truck in new FleetTruckRepository().LoadAll())
                {
                    var row = FleetRow.FromTruck(truck);
                    if (seen.Add(row.IdentityKey))
                        rows.Add(row);
                }

                var trucks = rows
                    .OrderBy(r => ParseTruckNumberForSort(r.TruckNumber))
                    .ThenBy(r => r.TruckNumber, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _grid.ItemsSource = trucks;
                _status.Text = trucks.Count == 0
                    ? "No fleet trucks registered yet. Add trucks in VTC Admin > Fleet Command Center."
                    : $"{trucks.Count} truck(s) loaded.";
            }
            catch (Exception ex)
            {
                _grid.ItemsSource = Array.Empty<FleetRow>();
                _status.Text = "Fleet unavailable: " + ex.Message;
            }
        }

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_grid.SelectedItem is not FleetRow row)
                return;

            var win = new VtcTruckProfileWindow(row.ToProfileTruck())
            {
                Owner = this
            };
            win.ShowDialog();
            LoadFleet();
        }

        private sealed class FleetRow
        {
            public FleetTruck? Truck { get; set; }
            public FleetCommandTruck? CommandTruck { get; set; }
            public string IdentityKey { get; set; } = "";
            public string TruckNumber { get; set; } = "";
            public string MakeModel { get; set; } = "";
            public string AssignedDriver { get; set; } = "";
            public string MileageDisplay { get; set; } = "";
            public string FuelDisplay { get; set; } = "";
            public string ConditionDisplay { get; set; } = "";
            public string Status { get; set; } = "";

            public FleetTruck ToProfileTruck()
            {
                if (Truck != null)
                    return Truck;

                var c = CommandTruck ?? new FleetCommandTruck();
                return new FleetTruck
                {
                    Plate = FirstNonEmpty(c.TruckNumber, c.PlateNumber, c.TruckName, c.Id),
                    Nickname = c.TruckName ?? "",
                    MakeModel = FirstNonEmpty(c.Model, c.ModName),
                    AssignedDriver = c.AssignedDriver ?? "",
                    OdometerMiles = c.OdometerMiles,
                    FuelPct = c.FuelPercent,
                    FuelPercent = c.FuelPercent,
                    ConditionPercent = c.HealthPercent,
                    LastKnownLocation = c.Location ?? "",
                    IsOnline = c.IsOnline,
                    IsDriving = c.IsDriving,
                    NeedsService = string.Equals(c.Status, "Needs Service", StringComparison.OrdinalIgnoreCase),
                    TotalFuelCost = c.TotalFuelCost,
                    TotalTollCost = c.TotalTollCost,
                    TotalMaintenanceCost = c.TotalMaintenanceCost,
                    TotalRepairCost = c.TotalRepairCost,
                    LastFuelUtc = c.LastFuelUtc,
                    LastTollUtc = c.LastTollUtc,
                    LastMaintenanceUtc = c.LastMaintenanceUtc,
                    LastRepairUtc = c.LastRepairUtc,
                    LastTelemetryUtc = c.UpdatedUtc
                };
            }

            public static FleetRow FromCommandTruck(FleetCommandTruck t)
            {
                var truckNumber = FirstNonEmpty(t.TruckNumber, t.PlateNumber, t.TruckName, t.Id, "--");
                return new FleetRow
                {
                    CommandTruck = t,
                    IdentityKey = BuildKey(truckNumber, t.PlateNumber, t.Id),
                    TruckNumber = truckNumber,
                    MakeModel = FirstNonEmpty(t.Model, t.ModName, t.TruckName, "--"),
                    AssignedDriver = string.IsNullOrWhiteSpace(t.AssignedDriver) ? "Unassigned" : t.AssignedDriver,
                    MileageDisplay = $"{t.OdometerMiles:0} mi",
                    FuelDisplay = $"{Math.Max(0, Math.Min(100, t.FuelPercent)):0}%",
                    ConditionDisplay = $"{Math.Max(0, Math.Min(100, t.HealthPercent)):0}%",
                    Status = string.IsNullOrWhiteSpace(t.Status) ? (t.IsOnline ? (t.IsDriving ? "Driving" : "Online") : "Offline") : t.Status
                };
            }

            public static FleetRow FromTruck(FleetTruck t)
            {
                var condition = t.ConditionPercent > 0 ? t.ConditionPercent : 100 - new[] { t.EngineDamagePct, t.TransmissionDamagePct, t.CabinDamagePct, t.ChassisDamagePct, t.WheelsDamagePct }.DefaultIfEmpty(0).Max();
                var fuel = t.FuelPct > 0 ? t.FuelPct : t.FuelPercent;
                var truckNumber = string.IsNullOrWhiteSpace(t.Plate) ? "--" : t.Plate;
                return new FleetRow
                {
                    Truck = t,
                    IdentityKey = BuildKey(truckNumber, t.Plate, t.Nickname),
                    TruckNumber = truckNumber,
                    MakeModel = string.IsNullOrWhiteSpace(t.MakeModel) ? t.Nickname : t.MakeModel,
                    AssignedDriver = string.IsNullOrWhiteSpace(t.AssignedDriver) ? "Unassigned" : t.AssignedDriver,
                    MileageDisplay = $"{t.OdometerMiles:0} mi",
                    FuelDisplay = $"{fuel:0}%",
                    ConditionDisplay = $"{Math.Max(0, Math.Min(100, condition)):0}%",
                    Status = t.IsOnline ? (t.IsDriving ? "Driving" : "Online") : "Offline"
                };
            }
        }

        private static string BuildKey(params string?[] values)
        {
            foreach (var value in values)
            {
                var text = (value ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(text) && text != "--")
                    return text;
            }

            return Guid.NewGuid().ToString("N");
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                var text = (value ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return "";
        }

        private static int ParseTruckNumberForSort(string? truckNumber)
        {
            var text = (truckNumber ?? "").Trim();
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(?:TRK-|TRUCK-|UNIT-|#)?(\d+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
                return n;

            return int.MaxValue;
        }
    }
}
