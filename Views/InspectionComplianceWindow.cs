using OverWatchELD.Models;
using OverWatchELD.Stores;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Views
{
    public sealed class InspectionComplianceWindow : Window
    {
        private readonly ObservableCollection<ComplianceRow> _rows = new();
        private readonly DataGrid _grid = new();
        private readonly TextBlock _stats = new();

        public InspectionComplianceWindow()
        {
            Title = "Fleet Inspection Compliance";
            Width = 1120;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07101F");

            Content = Build();
            Loaded += (_, _) => Refresh();
        }

        private UIElement Build()
        {
            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(Text("Fleet Inspection Compliance", 28, "#F8FAFC", true));

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 14, 0, 14)
            };
            Grid.SetRow(actions, 1);

            actions.Children.Add(Button("Refresh", "#2563EB", (_, _) => Refresh()));
            actions.Children.Add(Button("Export Violations Summary", "#166534", (_, _) => ExportSummary()));
            actions.Children.Add(Button("Close", "#334155", (_, _) => Close()));

            root.Children.Add(actions);

            _grid.ItemsSource = _rows;
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.IsReadOnly = true;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.Background = Brush("#0B1220");
            _grid.Foreground = Brushes.White;
            _grid.RowBackground = Brush("#0B1220");
            _grid.AlternatingRowBackground = Brush("#111827");
            _grid.BorderBrush = Brush("#334155");
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;

            _grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new System.Windows.Data.Binding(nameof(ComplianceRow.StatusIcon)), Width = 80 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Driver", Binding = new System.Windows.Data.Binding(nameof(ComplianceRow.Driver)), Width = 170 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Truck", Binding = new System.Windows.Data.Binding(nameof(ComplianceRow.Truck)), Width = 190 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Unit", Binding = new System.Windows.Data.Binding(nameof(ComplianceRow.Unit)), Width = 90 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Last Pre-Trip", Binding = new System.Windows.Data.Binding(nameof(ComplianceRow.LastPreTrip)), Width = 150 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Last Post-Trip", Binding = new System.Windows.Data.Binding(nameof(ComplianceRow.LastPostTrip)), Width = 150 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Defects", Binding = new System.Windows.Data.Binding(nameof(ComplianceRow.Defects)), Width = 120 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Open Tickets", Binding = new System.Windows.Data.Binding(nameof(ComplianceRow.OpenTickets)), Width = 120 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Compliance", Binding = new System.Windows.Data.Binding(nameof(ComplianceRow.ComplianceText)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            _stats.Foreground = Brush("#9CA3AF");
            _stats.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(_stats, 3);
            root.Children.Add(_stats);

            return root;
        }

        private void Refresh()
        {
            _rows.Clear();

            var inspections = new InspectionRecordStore().LoadAll();
            var tickets = new MaintenanceRequestTicketStore().LoadAll();

            var keys = inspections
                .Select(x => new
                {
                    Driver = Clean(x.DriverName),
                    Truck = Clean(x.TruckName),
                    Unit = Clean(x.UnitNumber)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Driver) || !string.IsNullOrWhiteSpace(x.Truck) || !string.IsNullOrWhiteSpace(x.Unit))
                .DistinctBy(x => $"{x.Driver}|{x.Truck}|{x.Unit}")
                .ToList();

            foreach (var key in keys)
            {
                var related = inspections.Where(x =>
                    Same(x.DriverName, key.Driver) ||
                    Same(x.TruckName, key.Truck) ||
                    Same(x.UnitNumber, key.Unit)).ToList();

                var lastPre = related
                    .Where(x => x.InspectionType.Contains("Pre", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.CreatedUtc)
                    .FirstOrDefault();

                var lastPost = related
                    .Where(x => x.InspectionType.Contains("Post", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.CreatedUtc)
                    .FirstOrDefault();

                var defectCount = related.Count(x => !x.Passed);

                var openTickets = tickets.Count(t =>
                    string.Equals(t.Status, "Open", StringComparison.OrdinalIgnoreCase) &&
                    (Same(t.DriverName, key.Driver) || Same(t.TruckName, key.Truck) || Same(t.UnitNumber, key.Unit)));

                var missingPre = lastPre == null || (DateTime.UtcNow - lastPre.CreatedUtc).TotalHours > 24;
                var hasDefects = defectCount > 0 || openTickets > 0;

                var status = hasDefects ? "Red" : missingPre ? "Yellow" : "Green";

                _rows.Add(new ComplianceRow
                {
                    Driver = Blank(key.Driver, "Unknown Driver"),
                    Truck = Blank(key.Truck, "Unknown Truck"),
                    Unit = Blank(key.Unit, "N/A"),
                    LastPreTrip = lastPre?.CreatedLocalDisplay ?? "Missing",
                    LastPostTrip = lastPost?.CreatedLocalDisplay ?? "Missing",
                    Defects = defectCount.ToString(),
                    OpenTickets = openTickets.ToString(),
                    StatusIcon = status == "Red" ? "🔴" : status == "Yellow" ? "🟡" : "🟢",
                    ComplianceText = status == "Red"
                        ? "Needs repair / unresolved defect"
                        : status == "Yellow"
                            ? "Missing recent pre-trip"
                            : "Compliant"
                });
            }

            var total = _rows.Count;
            var green = _rows.Count(x => x.StatusIcon == "🟢");
            var yellow = _rows.Count(x => x.StatusIcon == "🟡");
            var red = _rows.Count(x => x.StatusIcon == "🔴");

            _stats.Text = $"Total: {total} • Compliant: {green} • Missing Pre-Trip: {yellow} • Defect/Open Repair: {red}";
        }

        private void ExportSummary()
        {
            var bad = _rows.Where(x => x.StatusIcon != "🟢").ToList();

            if (bad.Count == 0)
            {
                MessageBox.Show("No compliance violations found.", "Inspection Compliance");
                return;
            }

            var sb = new StringBuilder();
            foreach (var row in bad)
                sb.AppendLine($"{row.StatusIcon} {row.Driver} | Unit {row.Unit} | {row.ComplianceText}");

            Clipboard.SetText(sb.ToString());

            MessageBox.Show(
                "Violation summary copied to clipboard.",
                "Inspection Compliance",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private static bool Same(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            !string.IsNullOrWhiteSpace(b) &&
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string Clean(string? value) => (value ?? "").Trim();

        private static string Blank(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static TextBlock Text(string text, double size, string color, bool bold) => new()
        {
            Text = text,
            FontSize = size,
            Foreground = Brush(color),
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal
        };

        private static Button Button(string text, string color, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = text,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 4, 12, 4),
                Background = Brush(color),
                Foreground = Brushes.White,
                BorderBrush = Brush("#38BDF8"),
                FontWeight = FontWeights.SemiBold
            };

            b.Click += click;
            return b;
        }

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        private sealed class ComplianceRow
        {
            public string StatusIcon { get; set; } = "";
            public string Driver { get; set; } = "";
            public string Truck { get; set; } = "";
            public string Unit { get; set; } = "";
            public string LastPreTrip { get; set; } = "";
            public string LastPostTrip { get; set; } = "";
            public string Defects { get; set; } = "";
            public string OpenTickets { get; set; } = "";
            public string ComplianceText { get; set; } = "";
        }
    }
}