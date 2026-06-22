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
    public sealed class TruckProfitabilityLeaderboardWindow : Window
    {
        private readonly DataGrid _truckGrid = new();
        private readonly DataGrid _driverGrid = new();
        private readonly DataGrid _revenueGrid = new();
        private readonly DataGrid _payrollGrid = new();
        private readonly DataGrid _problemTruckGrid = new();

        private readonly TextBlock _summaryText = new();

        public TruckProfitabilityLeaderboardWindow()
        {
            Title = "Truck Profitability + Driver Leaderboards";
            Width = 1240;
            Height = 780;
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
                Text = "Truck Profitability + Driver Leaderboards",
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

            foreach (var grid in new[] { _truckGrid, _driverGrid, _revenueGrid, _payrollGrid, _problemTruckGrid })
                SetupGrid(grid);

            var tabs = new TabControl
            {
                Background = Brush("#0D1A2B"),
                BorderBrush = Brush("#263E5C")
            };

            tabs.Items.Add(new TabItem { Header = "Truck Profitability", Content = _truckGrid });
            tabs.Items.Add(new TabItem { Header = "Driver Profit Ranking", Content = _driverGrid });
            tabs.Items.Add(new TabItem { Header = "Top Revenue Drivers", Content = _revenueGrid });
            tabs.Items.Add(new TabItem { Header = "Top Paid Drivers", Content = _payrollGrid });
            tabs.Items.Add(new TabItem { Header = "Problem Trucks", Content = _problemTruckGrid });

            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var sync = Button("Sync Economy");
            sync.Click += (_, _) =>
            {
                RealDriverEconomyPayrollService.SyncDeliveredLoadsAndPayroll();
                Refresh();
                MessageBox.Show("Economy synced from real delivered loads.", "Leaderboards");
            };

            var refresh = Button("Refresh");
            refresh.Click += (_, _) => Refresh();

            var close = Button("Close");
            close.Click += (_, _) => Close();

            buttons.Children.Add(sync);
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
                MinWidth = 110,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brush("#163B65"),
                BorderBrush = Brush("#4A91D0"),
                Foreground = Brushes.White,
                Padding = new Thickness(12, 4, 12, 4)
            };
        }

        private void Refresh()
        {
            var trucks = TruckProfitabilityLeaderboardService.BuildTruckProfitability();
            var drivers = TruckProfitabilityLeaderboardService.BuildDriverLeaderboard();
            var revenue = TruckProfitabilityLeaderboardService.BuildRevenueLeaderboard();
            var payroll = TruckProfitabilityLeaderboardService.BuildPayrollLeaderboard();
            var problem = TruckProfitabilityLeaderboardService.BuildProblemTruckList();

            var totalRevenue = trucks.Sum(x => x.GrossRevenue);
            var totalCost = trucks.Sum(x => x.TotalCost);
            var totalProfit = trucks.Sum(x => x.NetProfit);
            var totalMiles = trucks.Sum(x => x.MilesDriven);

            _summaryText.Text =
                $"Trucks: {trucks.Count}   Drivers: {drivers.Count}   " +
                $"Loads: {drivers.Sum(x => x.LoadsDelivered):N0}   Miles: {totalMiles:N0}   " +
                $"Revenue: {Money(totalRevenue)}   Costs: {Money(totalCost)}   Profit: {Money(totalProfit)}";

            _truckGrid.ItemsSource = trucks.Select(x => new
            {
                Truck = FirstNonEmpty(x.TruckNumber, x.TruckName),
                Driver = x.PrimaryDriver,
                Loads = x.LoadsDelivered,
                Miles = x.MilesDriven.ToString("N0"),
                Revenue = Money(x.GrossRevenue),
                Payroll = Money(x.PayrollCost),
                Fuel = Money(x.FuelCost),
                Maintenance = Money(x.MaintenanceCost),
                Other = Money(x.OtherCost),
                Profit = Money(x.NetProfit),
                RevPerMile = Money2(x.RevenuePerMile),
                ProfitPerMile = Money2(x.ProfitPerMile),
                LastActivity = x.LastActivityUtc?.ToLocalTime().ToString("g") ?? ""
            }).ToList();

            _driverGrid.ItemsSource = drivers.Select(x => new
            {
                x.Rank,
                Driver = x.DriverName,
                Loads = x.LoadsDelivered,
                Miles = x.MilesDriven.ToString("N0"),
                Revenue = Money(x.GrossRevenue),
                Payroll = Money(x.PayrollPaid),
                Profit = Money(x.CompanyProfit),
                RevPerMile = Money2(x.RevenuePerMile),
                ProfitPerMile = Money2(x.ProfitPerMile),
                Truck = FirstNonEmpty(x.TruckNumber, x.TruckName),
                LastDelivery = x.LastDeliveryUtc?.ToLocalTime().ToString("g") ?? ""
            }).ToList();

            _revenueGrid.ItemsSource = revenue.Select(x => new
            {
                x.Rank,
                Driver = x.DriverName,
                Revenue = Money(x.GrossRevenue),
                Loads = x.LoadsDelivered,
                Miles = x.MilesDriven.ToString("N0"),
                Payroll = Money(x.PayrollPaid),
                Profit = Money(x.CompanyProfit),
                Truck = FirstNonEmpty(x.TruckNumber, x.TruckName)
            }).ToList();

            _payrollGrid.ItemsSource = payroll.Select(x => new
            {
                x.Rank,
                Driver = x.DriverName,
                Payroll = Money(x.PayrollPaid),
                Revenue = Money(x.GrossRevenue),
                Loads = x.LoadsDelivered,
                Miles = x.MilesDriven.ToString("N0"),
                Truck = FirstNonEmpty(x.TruckNumber, x.TruckName)
            }).ToList();

            _problemTruckGrid.ItemsSource = problem.Select(x => new
            {
                Truck = FirstNonEmpty(x.TruckNumber, x.TruckName),
                Driver = x.PrimaryDriver,
                Profit = Money(x.NetProfit),
                Revenue = Money(x.GrossRevenue),
                Costs = Money(x.TotalCost),
                Maintenance = Money(x.MaintenanceCost),
                Fuel = Money(x.FuelCost),
                Loads = x.LoadsDelivered,
                Miles = x.MilesDriven.ToString("N0")
            }).ToList();
        }

        private static string Money(decimal value)
        {
            return value.ToString("C0", CultureInfo.CurrentCulture);
        }

        private static string Money2(decimal value)
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
