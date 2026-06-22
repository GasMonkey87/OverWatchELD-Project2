using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OverWatchELD.Models.Fleet;

namespace OverWatchELD.Views
{
    public sealed class VtcTruckProfileWindow : Window
    {
        private readonly FleetTruck _truck;

        public VtcTruckProfileWindow(FleetTruck truck)
        {
            _truck = truck;
            Title = $"Truck Profile - {_truck.Plate}";
            Width = 1050;
            Height = 760;
            MinWidth = 820;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.Black;
            Content = BuildContent();
        }

        private UIElement BuildContent()
        {
            var root = new DockPanel { Margin = new Thickness(14) };

            var title = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            title.Children.Add(new TextBlock { Text = $"Truck #{_truck.Plate}", Foreground = Brushes.White, FontSize = 26, FontWeight = FontWeights.SemiBold });
            title.Children.Add(new TextBlock { Text = $"{Value(_truck.MakeModel)}  •  Driver: {Value(_truck.AssignedDriver, "Unassigned")}", Foreground = Brushes.LightGray, Margin = new Thickness(0, 4, 0, 0) });
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);

            var tabs = new TabControl { Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)), Foreground = Brushes.White };
            tabs.Items.Add(new TabItem { Header = "Stats", Content = BuildStatsTab() });
            tabs.Items.Add(new TabItem { Header = "Maintenance History", Content = BuildMaintenanceTab() });
            tabs.Items.Add(new TabItem { Header = "Loads", Content = BuildLoadsTab() });
            tabs.Items.Add(new TabItem { Header = "Fuel / Tolls", Content = BuildFuelTollsTab() });
            root.Children.Add(tabs);

            return root;
        }

        private UIElement BuildStatsTab()
        {
            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            var right = new StackPanel();
            Grid.SetColumn(right, 1);

            AddStat(left, "Truck Number", _truck.Plate);
            AddStat(left, "Make / Model", Value(_truck.MakeModel));
            AddStat(left, "Nickname", Value(_truck.Nickname));
            AddStat(left, "Assigned Driver", Value(_truck.AssignedDriver, "Unassigned"));
            AddStat(left, "Mileage", $"{_truck.OdometerMiles:0} mi");
            AddStat(left, "Location", Value(_truck.LastKnownLocation ?? JoinLocation(_truck.LastKnownCity, _truck.LastKnownState)));

            AddStat(right, "Fuel", $"{(_truck.FuelPct > 0 ? _truck.FuelPct : _truck.FuelPercent):0}%");
            AddStat(right, "Online", _truck.IsOnline ? "Yes" : "No");
            AddStat(right, "Driving", _truck.IsDriving ? "Yes" : "No");
            AddStat(right, "Engine Damage", $"{_truck.EngineDamagePct:0}%");
            AddStat(right, "Transmission Damage", $"{_truck.TransmissionDamagePct:0}%");
            AddStat(right, "Cabin / Chassis / Wheels", $"{_truck.CabinDamagePct:0}% / {_truck.ChassisDamagePct:0}% / {_truck.WheelsDamagePct:0}%");
            AddStat(right, "Last Telemetry", _truck.LastTelemetryUtc == DateTimeOffset.MinValue ? "Never" : _truck.LastTelemetryUtc.LocalDateTime.ToString("g"));

            grid.Children.Add(left);
            grid.Children.Add(right);
            return new ScrollViewer { Content = grid };
        }

        private UIElement BuildMaintenanceTab()
        {
            var grid = MakeGrid();
            grid.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new System.Windows.Data.Binding("DateDisplay"), Width = new DataGridLength(1.1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new System.Windows.Data.Binding("Type"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Mileage", Binding = new System.Windows.Data.Binding("MileageDisplay"), Width = new DataGridLength(1.0, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Cost", Binding = new System.Windows.Data.Binding("CostDisplay"), Width = new DataGridLength(0.8, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Notes", Binding = new System.Windows.Data.Binding("Notes"), Width = new DataGridLength(2.2, DataGridLengthUnitType.Star) });
            grid.ItemsSource = (_truck.Maintenance ?? new System.Collections.Generic.List<MaintenanceRecord>()).Select(x => new
            {
                DateDisplay = x.DateUtc.LocalDateTime.ToString("g"),
                x.Type,
                MileageDisplay = $"{x.Mileage:0} mi",
                CostDisplay = x.Cost.ToString("C"),
                x.Notes
            }).ToList();
            return grid;
        }

        private UIElement BuildLoadsTab()
        {
            return new TextBlock
            {
                Text = "Loads assigned to this truck will appear here when load history is linked by truck number.",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(14),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private UIElement BuildFuelTollsTab()
        {
            var tabs = new TabControl { Margin = new Thickness(8) };
            var fuel = MakeGrid();
            fuel.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new System.Windows.Data.Binding("DateDisplay"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
            fuel.Columns.Add(new DataGridTextColumn { Header = "Mileage", Binding = new System.Windows.Data.Binding("MileageDisplay"), Width = new DataGridLength(1.0, DataGridLengthUnitType.Star) });
            fuel.Columns.Add(new DataGridTextColumn { Header = "Fuel After", Binding = new System.Windows.Data.Binding("FuelDisplay"), Width = new DataGridLength(1.0, DataGridLengthUnitType.Star) });
            fuel.Columns.Add(new DataGridTextColumn { Header = "Cost", Binding = new System.Windows.Data.Binding("CostDisplay"), Width = new DataGridLength(0.8, DataGridLengthUnitType.Star) });
            fuel.Columns.Add(new DataGridTextColumn { Header = "Notes", Binding = new System.Windows.Data.Binding("Notes"), Width = new DataGridLength(2.0, DataGridLengthUnitType.Star) });
            fuel.ItemsSource = (_truck.FuelLog ?? new System.Collections.Generic.List<FuelRecord>()).Select(x => new
            {
                DateDisplay = x.DateUtc.LocalDateTime.ToString("g"),
                MileageDisplay = $"{x.OdometerMiles:0} mi",
                FuelDisplay = $"{x.FuelPctAfter:0}%",
                CostDisplay = x.Cost.ToString("C"),
                x.Notes
            }).ToList();

            var tolls = MakeGrid();
            tolls.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new System.Windows.Data.Binding("DateDisplay"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
            tolls.Columns.Add(new DataGridTextColumn { Header = "Mileage", Binding = new System.Windows.Data.Binding("MileageDisplay"), Width = new DataGridLength(1.0, DataGridLengthUnitType.Star) });
            tolls.Columns.Add(new DataGridTextColumn { Header = "Cost", Binding = new System.Windows.Data.Binding("CostDisplay"), Width = new DataGridLength(0.8, DataGridLengthUnitType.Star) });
            tolls.Columns.Add(new DataGridTextColumn { Header = "Notes", Binding = new System.Windows.Data.Binding("Notes"), Width = new DataGridLength(2.0, DataGridLengthUnitType.Star) });
            tolls.ItemsSource = (_truck.TollLog ?? new System.Collections.Generic.List<TollRecord>()).Select(x => new
            {
                DateDisplay = x.DateUtc.LocalDateTime.ToString("g"),
                MileageDisplay = $"{x.OdometerMiles:0} mi",
                CostDisplay = x.Cost.ToString("C"),
                x.Notes
            }).ToList();

            tabs.Items.Add(new TabItem { Header = "Fuel", Content = fuel });
            tabs.Items.Add(new TabItem { Header = "Tolls", Content = tolls });
            return tabs;
        }

        private static DataGrid MakeGrid()
        {
            return new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                RowHeaderWidth = 0,
                Margin = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                Foreground = Brushes.White,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
                RowBackground = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42))
            };
        }

        private static void AddStat(Panel panel, string label, string value)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 18, 12) };
            block.Children.Add(new TextBlock { Text = label, Foreground = Brushes.Gray, FontSize = 12 });
            block.Children.Add(new TextBlock { Text = value, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(block);
        }

        private static string Value(string? value, string fallback = "--") => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        private static string JoinLocation(string? city, string? state)
        {
            var joined = string.Join(", ", new[] { city, state }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
            return string.IsNullOrWhiteSpace(joined) ? "--" : joined;
        }
    }
}
