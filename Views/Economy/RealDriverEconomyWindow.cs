using OverWatchELD.Models.Economy;
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
    public sealed class RealDriverEconomyWindow : Window
    {
        private readonly DataGrid _driversGrid = new();
        private readonly DataGrid _profilesGrid = new();
        private readonly TextBlock _summaryText = new();

        public RealDriverEconomyWindow()
        {
            Title = "Real Driver Economy + Payroll";
            Width = 1180;
            Height = 760;
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
                Text = "Real Driver Economy + Payroll",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold
            };

            Grid.SetRow(title, 0);
            root.Children.Add(title);

            _summaryText.Foreground = Brush("#9FB3CC");
            _summaryText.FontSize = 14;
            _summaryText.Margin = new Thickness(0, 8, 0, 14);
            Grid.SetRow(_summaryText, 1);
            root.Children.Add(_summaryText);

            var tabs = new TabControl
            {
                Background = Brush("#0D1A2B"),
                BorderBrush = Brush("#263E5C")
            };

            SetupGrid(_driversGrid);
            SetupGrid(_profilesGrid);

            tabs.Items.Add(new TabItem
            {
                Header = "Driver Summary",
                Content = _driversGrid
            });

            tabs.Items.Add(new TabItem
            {
                Header = "Payroll Profiles",
                Content = _profilesGrid
            });

            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var sync = Button("Sync Delivered Loads");
            sync.Click += (_, _) =>
            {
                RealDriverEconomyPayrollService.SyncDeliveredLoadsAndPayroll();
                Refresh();
                MessageBox.Show("Real driver economy/payroll synced from delivered loads.", "Real Driver Economy");
            };

            var createProfiles = Button("Create Missing Profiles");
            createProfiles.Click += (_, _) =>
            {
                foreach (var row in RealDriverEconomyPayrollService.BuildDriverSummaries())
                    RealDriverPayrollStore.GetOrCreate(row.DriverName, row.DriverDiscordId);

                Refresh();
            };

            var refresh = Button("Refresh");
            refresh.Click += (_, _) => Refresh();

            var close = Button("Close");
            close.Click += (_, _) => Close();

            buttons.Children.Add(sync);
            buttons.Children.Add(createProfiles);
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
                MinWidth = 120,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brush("#163B65"),
                BorderBrush = Brush("#4A91D0"),
                Foreground = Brushes.White,
                Padding = new Thickness(12, 4, 12, 4)
            };
        }

        private void Refresh()
        {
            var rows = RealDriverEconomyPayrollService.BuildDriverSummaries();

            var gross = rows.Sum(x => x.GrossRevenue);
            var payroll = rows.Sum(x => x.PayrollPaid);
            var profit = rows.Sum(x => x.CompanyProfit);

            _summaryText.Text =
                $"Drivers: {rows.Count}   Loads Delivered: {rows.Sum(x => x.LoadsDelivered):N0}   " +
                $"Miles: {rows.Sum(x => x.MilesDriven):N0}   Gross Revenue: {Money(gross)}   " +
                $"Payroll: {Money(payroll)}   Company Profit: {Money(profit)}";

            _driversGrid.ItemsSource = rows
                .Select(x => new
                {
                    Driver = x.DriverName,
                    Loads = x.LoadsDelivered,
                    Miles = x.MilesDriven.ToString("N0"),
                    Revenue = Money(x.GrossRevenue),
                    Payroll = Money(x.PayrollPaid),
                    Profit = Money(x.CompanyProfit),
                    Truck = FirstNonEmpty(x.TruckNumber, x.TruckName),
                    LastDelivery = x.LastDeliveryUtc?.ToLocalTime().ToString("g") ?? ""
                })
                .ToList();

            _profilesGrid.ItemsSource = RealDriverPayrollStore.Load()
                .OrderBy(x => x.DriverName)
                .Select(x => new
                {
                    Driver = x.DriverName,
                    x.PayMode,
                    Percent = x.PercentOfLoad.ToString("0.##") + "%",
                    PerMile = (x.CentsPerMile / 100m).ToString("C2") + " / mi",
                    FlatLoad = Money(x.FlatPerLoad),
                    Bonus = x.SafetyBonusPercent.ToString("0.##") + "%",
                    x.Enabled,
                    Updated = x.UpdatedUtc.ToLocalTime().ToString("g")
                })
                .ToList();
        }

        private static string Money(decimal value)
        {
            return value.ToString("C0", CultureInfo.CurrentCulture);
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
