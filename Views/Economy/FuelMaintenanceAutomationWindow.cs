using OverWatchELD.Services.Economy;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OverWatchELD.Views.Economy
{
    public sealed class FuelMaintenanceAutomationWindow : Window
    {
        private readonly DataGrid _snapshotGrid = new();
        private readonly DataGrid _transactionGrid = new();
        private readonly TextBlock _summaryText = new();

        public FuelMaintenanceAutomationWindow()
        {
            Title = "Fuel + Maintenance Automation";
            Width = 1180;
            Height = 740;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07111F");

            Content = BuildLayout();

            Loaded += (_, _) => Refresh();
        }

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(18) };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Fuel + Maintenance Automation",
                Foreground = Brushes.White,
                FontSize = 27,
                FontWeight = FontWeights.Bold
            };

            Grid.SetRow(title, 0);
            root.Children.Add(title);

            _summaryText.Foreground = Brush("#9FB3CC");
            _summaryText.FontSize = 14;
            _summaryText.Margin = new Thickness(0, 8, 0, 14);
            Grid.SetRow(_summaryText, 1);
            root.Children.Add(_summaryText);

            SetupGrid(_snapshotGrid);
            SetupGrid(_transactionGrid);

            var tabs = new TabControl
            {
                Background = Brush("#07111F"),
                BorderBrush = Brush("#263E5C"),
                Foreground = Brushes.White
            };

            tabs.Resources[typeof(TabItem)] = new Style(typeof(TabItem))
            {
                Setters =
    {
        new Setter(Control.BackgroundProperty, Brush("#102038")),
        new Setter(Control.ForegroundProperty, Brushes.White),
        new Setter(Control.BorderBrushProperty, Brush("#263E5C")),
        new Setter(Control.PaddingProperty, new Thickness(18,8,18,8)),
        new Setter(Control.FontWeightProperty, FontWeights.SemiBold)
    },

                Triggers =
    {
        new Trigger
        {
            Property = TabItem.IsSelectedProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, Brush("#163B65")),
                new Setter(Control.ForegroundProperty, Brushes.White),
                new Setter(Control.BorderBrushProperty, Brush("#4A91D0"))
            }
        }
    }
            };

            tabs.Items.Add(new TabItem { Header = "Truck Snapshots", Content = _snapshotGrid });
            tabs.Items.Add(new TabItem { Header = "Auto Expenses", Content = _transactionGrid });

            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var process = Button("Process Current Telemetry");
            process.Click += (_, _) =>
            {
                FuelMaintenanceAutomationService.ProcessCurrentTelemetryIfAvailable();
                Refresh();
                MessageBox.Show("Current telemetry processed if available.", "Fuel + Maintenance Automation");
            };

            var refresh = Button("Refresh");
            refresh.Click += (_, _) => Refresh();

            var close = Button("Close");
            close.Click += (_, _) => Close();

            buttons.Children.Add(process);
            buttons.Children.Add(refresh);
            buttons.Children.Add(close);

            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            return root;
        }

        private void SetupGrid(DataGrid grid)
        {
            grid.AutoGenerateColumns = true;
            grid.IsReadOnly = true;
            grid.CanUserAddRows = false;
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.Background = Brush("#0D1A2B");
            grid.Foreground = Brushes.White;
            grid.RowBackground = Brush("#0B1626");
            grid.AlternatingRowBackground = Brush("#102038");
            grid.BorderBrush = Brush("#263E5C");
            grid.HorizontalGridLinesBrush = Brush("#263E5C");
            grid.VerticalGridLinesBrush = Brush("#1A2D45");
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;

            grid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, Brush("#132238")),
                    new Setter(Control.ForegroundProperty, Brushes.White),
                    new Setter(Control.BorderBrushProperty, Brush("#263E5C")),
                    new Setter(Control.FontWeightProperty, FontWeights.SemiBold),
                    new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8))
                }
            };

            grid.CellStyle = new Style(typeof(DataGridCell))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, Brushes.Transparent),
                    new Setter(Control.ForegroundProperty, Brushes.White),
                    new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6))
                }
            };
        }

        private Button Button(string text)
        {
            return new Button
            {
                Content = text,
                Height = 38,
                MinWidth = 130,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brush("#163B65"),
                BorderBrush = Brush("#4A91D0"),
                Foreground = Brushes.White,
                Padding = new Thickness(12, 4, 12, 4)
            };
        }

        private void Refresh()
        {
            var snapshots = FuelMaintenanceAutomationService.LoadSnapshots();
            var tx = EconomyStore.LoadTransactions()
                .Where(x =>
                    string.Equals(x.Source, "Fuel/Maintenance Automation", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Type, "AutoFuelExpense", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Type, "AutoWearReserve", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Type, "AutoRepairReserve", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedUtc)
                .ToList();

            var fuel = tx.Where(x => string.Equals(x.Category, "Fuel", StringComparison.OrdinalIgnoreCase)).Sum(x => Math.Abs(x.Amount));
            var maintenance = tx.Where(x => string.Equals(x.Category, "Maintenance", StringComparison.OrdinalIgnoreCase)).Sum(x => Math.Abs(x.Amount));

            _summaryText.Text =
                $"Tracked Trucks: {snapshots.Count}   Auto Transactions: {tx.Count:N0}   " +
                $"Fuel: {Money(fuel)}   Maintenance/Wear: {Money(maintenance)}   " +
                $"Diesel: {Money(FuelMaintenanceAutomationService.DieselPricePerGallon)}/gal   Wear: {Money(FuelMaintenanceAutomationService.WearReservePerMile)}/mi";

            _snapshotGrid.ItemsSource = snapshots
                .OrderBy(x => FirstNonEmpty(x.TruckNumber, x.TruckName))
                .Select(x => new
                {
                    Truck = FirstNonEmpty(x.TruckNumber, x.TruckName, x.TruckKey),
                    Driver = x.DriverName,
                    Fuel = x.FuelPercent?.ToString("0.##") + "%",
                    Odometer = x.OdometerMiles?.ToString("N0") ?? "",
                    Health = x.HealthPercent?.ToString("0.##") + "%",
                    Damage = x.DamagePercent?.ToString("0.##") + "%",
                    Updated = x.LastUpdatedUtc.ToLocalTime().ToString("g")
                })
                .ToList();

            _transactionGrid.ItemsSource = tx
                .Select(x => new
                {
                    Date = x.CreatedUtc.ToLocalTime().ToString("g"),
                    x.Type,
                    x.Category,
                    Amount = Money(x.Amount),
                    Truck = FirstNonEmpty(x.TruckNumber, x.TruckName),
                    Driver = x.DriverName,
                    x.Description,
                    x.Notes
                })
                .ToList();
        }

        private static string Money(decimal value)
        {
            return value.ToString("C2", CultureInfo.CurrentCulture);
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }
}
