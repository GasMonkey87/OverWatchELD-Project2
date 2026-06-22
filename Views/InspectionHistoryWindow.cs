using OverWatchELD.Models;
using OverWatchELD.Services;
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
    public sealed class InspectionHistoryWindow : Window
    {
        private readonly InspectionRecordStore _store = new();
        private readonly ObservableCollection<InspectionRow> _rows = new();

        private readonly DataGrid _grid = new();
        private readonly TextBlock _status = new();

        public InspectionHistoryWindow()
        {
            Title = "Inspection History";
            Width = 1050;
            Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07101F");

            Content = Build();
            Loaded += (_, _) => LoadRows();
        }

        private UIElement Build()
        {
            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(Text("Inspection History", 28, "#F8FAFC", true));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
            Grid.SetRow(actions, 1);

            actions.Children.Add(Button("New Pre-Trip", "#2563EB", (_, _) => AddInspection("Pre-Trip")));
            actions.Children.Add(Button("New Post-Trip", "#6D28D9", (_, _) => AddInspection("Post-Trip")));
            actions.Children.Add(Button("Other Inspection", "#0F766E", (_, _) => AddInspection("Other")));
            actions.Children.Add(Button("Export Selected to Discord", "#166534", async (_, _) => await ExportSelectedAsync()));
            actions.Children.Add(Button("Refresh", "#334155", (_, _) => LoadRows()));

            root.Children.Add(actions);

            _grid.ItemsSource = _rows;
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.Background = Brush("#0B1220");
            _grid.Foreground = Brushes.White;
            _grid.RowBackground = Brush("#0B1220");
            _grid.AlternatingRowBackground = Brush("#111827");
            _grid.BorderBrush = Brush("#334155");
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;

            _grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Export",
                Binding = new System.Windows.Data.Binding(nameof(InspectionRow.IsSelected)) { Mode = System.Windows.Data.BindingMode.TwoWay },
                Width = 70
            });

            _grid.Columns.Add(new DataGridTextColumn { Header = "Inspection #", Binding = new System.Windows.Data.Binding(nameof(InspectionRow.InspectionNumber)), Width = 150 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Date/Time", Binding = new System.Windows.Data.Binding(nameof(InspectionRow.CreatedLocalDisplay)), Width = 150 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new System.Windows.Data.Binding(nameof(InspectionRow.InspectionType)), Width = 100 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Truck", Binding = new System.Windows.Data.Binding(nameof(InspectionRow.TruckName)), Width = 150 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Unit", Binding = new System.Windows.Data.Binding(nameof(InspectionRow.UnitNumber)), Width = 90 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Driver", Binding = new System.Windows.Data.Binding(nameof(InspectionRow.DriverName)), Width = 150 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new System.Windows.Data.Binding(nameof(InspectionRow.StatusText)), Width = 120 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Notes", Binding = new System.Windows.Data.Binding(nameof(InspectionRow.Notes)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _grid.MouseDoubleClick += InspectionGrid_MouseDoubleClick;
            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            _status.Foreground = Brush("#9CA3AF");
            _status.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(_status, 3);
            root.Children.Add(_status);

            return root;
        }

        private void LoadRows()
        {
            _rows.Clear();

            foreach (var r in _store.LoadAll().OrderByDescending(x => x.CreatedUtc))
                _rows.Add(new InspectionRow(r));

            _status.Text = $"{_rows.Count} inspections loaded.";
        }

        private void InspectionGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (_grid.SelectedItem is not InspectionRow row)
                    return;

                string inspectionType = row.Model.InspectionType;

                if (string.IsNullOrWhiteSpace(inspectionType))
                    inspectionType = "Vehicle Inspection";

                var win = new InspectionEntryWindow();

                win.Owner = this;
                win.Tag = inspectionType;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                win.ShowDialog();

                LoadRows();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open inspection.\n\n" + ex.Message,
                    "Inspection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void AddInspection(string type)
        {
            try
            {
                var win = new InspectionEntryWindow();

                win.Owner = this;
                win.Tag = type;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                if (win.ShowDialog() == true)
                    LoadRows();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open inspection window.\n\n" + ex.Message,
                    "Inspection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task ExportSelectedAsync()
        {
            var selected = _rows.Where(x => x.IsSelected).Select(x => x.Model).ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show("Select at least one inspection to export.", "Inspections");
                return;
            }

            var ok = await new DiscordInspectionExportService().ExportAsync(selected);

            MessageBox.Show(
                ok ? "Inspections exported to Discord." : "Inspection export failed.",
                "Inspections",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

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

        private sealed class InspectionRow
        {
            public InspectionRecord Model { get; }
            public bool IsSelected { get; set; }
            public string InspectionNumber => Model.InspectionNumber;
            public string CreatedLocalDisplay => Model.CreatedLocalDisplay;
            public string InspectionType => Model.InspectionType;
            public string TruckName => Model.TruckName;
            public string UnitNumber => Model.UnitNumber;
            public string DriverName => Model.DriverName;
            public string StatusText => Model.StatusText;
            public string Notes => Model.Notes;

            public InspectionRow(InspectionRecord model)
            {
                Model = model;
                IsSelected = true;
            }
        }
    }
}