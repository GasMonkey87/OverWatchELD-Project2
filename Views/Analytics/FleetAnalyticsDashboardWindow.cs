using OverWatchELD.Models.Analytics;
using OverWatchELD.Services.Analytics;
using OverWatchELD.Services.Fleet;
using OverWatchELD.Stores;
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OverWatchELD.Views.Analytics
{
    public sealed class FleetAnalyticsDashboardWindow : Window
    {
        private readonly TextBlock _balanceText = new();
        private readonly TextBlock _profitText = new();
        private readonly TextBlock _weekText = new();
        private readonly TextBlock _monthText = new();
        private readonly TextBlock _rpmText = new();
        private readonly TextBlock _scoreText = new();

        private readonly DataGrid _trendGrid = new();
        private readonly DataGrid _expenseGrid = new();
        private readonly DataGrid _revenueGrid = new();
        private readonly DataGrid _topDriversGrid = new();
        private readonly DataGrid _topTrucksGrid = new();
        private readonly DataGrid _problemTrucksGrid = new();
        private readonly DataGrid _contractsGrid = new();
        private readonly DataGrid _fleetTrucksGrid = new();

        private readonly TextBox _fleetTruckSearchBox = new();
        private readonly TextBlock _fleetTruckSearchCount = new();
        private readonly TextBlock _footerText = new();

        public FleetAnalyticsDashboardWindow()
        {
            Title = "Fleet Analytics Dashboard";
            Width = 1280;
            Height = 820;
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
                Text = "Fleet Analytics Dashboard",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            };

            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var cards = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 16) };
            cards.Children.Add(Card("Balance", _balanceText));
            cards.Children.Add(Card("Net Profit", _profitText));
            cards.Children.Add(Card("This Week", _weekText));
            cards.Children.Add(Card("This Month", _monthText));
            cards.Children.Add(Card("Profit / Mile", _rpmText));
            cards.Children.Add(Card("Avg Driver Score", _scoreText));

            Grid.SetRow(cards, 1);
            root.Children.Add(cards);

            foreach (var grid in new[]
            {
                _trendGrid,
                _expenseGrid,
                _revenueGrid,
                _topDriversGrid,
                _topTrucksGrid,
                _problemTrucksGrid,
                _contractsGrid,
                _fleetTrucksGrid
            })
            {
                SetupGrid(grid);
            }

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
                    new Setter(Control.PaddingProperty, new Thickness(18, 8, 18, 8)),
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

            tabs.Items.Add(new TabItem { Header = "30-Day Trend", Content = _trendGrid });
            tabs.Items.Add(new TabItem { Header = "Expense Breakdown", Content = _expenseGrid });
            tabs.Items.Add(new TabItem { Header = "Revenue Breakdown", Content = _revenueGrid });
            tabs.Items.Add(new TabItem { Header = "Top Drivers", Content = _topDriversGrid });
            tabs.Items.Add(new TabItem { Header = "Top Trucks", Content = _topTrucksGrid });
            tabs.Items.Add(new TabItem { Header = "Problem Trucks", Content = _problemTrucksGrid });
            tabs.Items.Add(new TabItem { Header = "Top Contracts", Content = _contractsGrid });
            tabs.Items.Add(new TabItem { Header = "Fleet Trucks", Content = BuildFleetTrucksTab() });

            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };

            _footerText.Foreground = Brush("#9FB3CC");
            _footerText.VerticalAlignment = VerticalAlignment.Center;

            DockPanel.SetDock(_footerText, Dock.Left);
            footer.Children.Add(_footerText);

            var close = Button("Close");
            close.Click += (_, _) => Close();

            var refresh = Button("Refresh Analytics");
            refresh.Click += (_, _) => Refresh();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            buttons.Children.Add(refresh);
            buttons.Children.Add(close);

            DockPanel.SetDock(buttons, Dock.Right);
            footer.Children.Add(buttons);

            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            return root;
        }

        private UIElement BuildFleetTrucksTab()
        {
            var grid = new Grid();

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var searchPanel = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            var searchLabel = new TextBlock
            {
                Text = "Search Fleet Trucks:",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            DockPanel.SetDock(searchLabel, Dock.Left);
            searchPanel.Children.Add(searchLabel);

            _fleetTruckSearchBox.Width = 260;
            _fleetTruckSearchBox.Height = 34;
            _fleetTruckSearchBox.Padding = new Thickness(8, 4, 8, 4);
            _fleetTruckSearchBox.Background = Brush("#0D1A2B");
            _fleetTruckSearchBox.Foreground = Brushes.White;
            _fleetTruckSearchBox.BorderBrush = Brush("#263E5C");
            _fleetTruckSearchBox.TextChanged += (_, _) => Refresh();

            DockPanel.SetDock(_fleetTruckSearchBox, Dock.Left);
            searchPanel.Children.Add(_fleetTruckSearchBox);

            _fleetTruckSearchCount.Foreground = Brush("#9FB3CC");
            _fleetTruckSearchCount.VerticalAlignment = VerticalAlignment.Center;
            _fleetTruckSearchCount.Margin = new Thickness(14, 0, 0, 0);

            searchPanel.Children.Add(_fleetTruckSearchCount);

            var deleteButton = Button("Delete Truck");
            deleteButton.Background = Brush("#991B1B");
            deleteButton.BorderBrush = Brush("#EF4444");
            deleteButton.Margin = new Thickness(18, 0, 0, 0);
            deleteButton.Click += (_, _) => DeleteSelectedFleetTruck();

            DockPanel.SetDock(deleteButton, Dock.Right);
            searchPanel.Children.Add(deleteButton);

            Grid.SetRow(searchPanel, 0);
            grid.Children.Add(searchPanel);

            _fleetTrucksGrid.MouseDoubleClick += (_, _) => OpenFleetTruckDetails();

            Grid.SetRow(_fleetTrucksGrid, 1);
            grid.Children.Add(_fleetTrucksGrid);

            return grid;
        }

        private void DeleteSelectedFleetTruck()
        {
            if (_fleetTrucksGrid.SelectedItem == null)
            {
                MessageBox.Show(
                    "Select a truck first.",
                    "No Truck Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            dynamic truck = _fleetTrucksGrid.SelectedItem;

            var truckNumber = Convert.ToString(truck.TruckNumber) ?? "";
            var truckName = Convert.ToString(truck.Truck) ?? "";

            if (string.IsNullOrWhiteSpace(truckNumber))
            {
                MessageBox.Show(
                    "Selected truck does not have a valid truck number.",
                    "Delete Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete Truck #{truckNumber} - {truckName}?\n\nThis will remove it across the ELD system and cannot be undone.",
                "Delete Truck",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                var fleetStore = new FleetCommandStore();

                var fleetRows = fleetStore.LoadAll()
                    .Where(x =>
                        !string.Equals(
                            x.TruckNumber,
                            truckNumber,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                fleetStore.SaveAll(fleetRows);

                PendingFleetTruckApprovalStore.DeleteByTruckNumber(truckNumber);

                var maintenance = VtcMaintenanceStore.Load();

                if (maintenance != null)
                {
                    maintenance.Trucks = maintenance.Trucks
                        .Where(x =>
                            !string.Equals(
                                x.UnitNumber,
                                truckNumber,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    VtcMaintenanceStore.Save(maintenance);
                }

                MessageBox.Show(
                    $"Truck #{truckNumber} deleted successfully.",
                    "Truck Deleted",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Delete Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenFleetTruckDetails()
        {
            if (_fleetTrucksGrid.SelectedItem == null)
                return;

            dynamic truck = _fleetTrucksGrid.SelectedItem;

            var status = Convert.ToString(truck.Status) ?? "Unknown";
            var isPending = status.Contains("Pending", StringComparison.OrdinalIgnoreCase);

            var detailsWindow = new Window
            {
                Title = $"Fleet Truck • {truck.TruckNumber}",
                Width = 520,
                Height = isPending ? 420 : 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Brush("#07111F")
            };

            var root = new StackPanel
            {
                Margin = new Thickness(18)
            };

            root.Children.Add(new TextBlock
            {
                Text = $"{truck.Active}  Truck #{truck.TruckNumber}",
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 18)
            });

            root.Children.Add(CreateDetailRow("Status", truck.Status));
            root.Children.Add(CreateDetailRow("Truck", truck.Truck));
            root.Children.Add(CreateDetailRow("Model", truck.Model));
            root.Children.Add(CreateDetailRow("Driver", truck.Driver));
            root.Children.Add(CreateDetailRow("Plate", truck.Plate));

            if (!isPending)
            {
                root.Children.Add(CreateDetailRow("Mileage", truck.Miles));
                root.Children.Add(CreateDetailRow("Fuel", truck.Fuel));
                root.Children.Add(CreateDetailRow("Health", truck.Health));
                root.Children.Add(CreateDetailRow("Damage", truck.Damage));
                root.Children.Add(CreateDetailRow("Location", truck.Location));
            }
            else
            {
                root.Children.Add(new Border
                {
                    Margin = new Thickness(0, 20, 0, 0),
                    Padding = new Thickness(14),
                    Background = Brush("#3F2A00"),
                    BorderBrush = Brush("#FACC15"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Child = new TextBlock
                    {
                        Text =
                            "This truck is still pending approval.\n\nDetailed fleet telemetry and statistics are hidden until approved by management.",
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeights.SemiBold
                    }
                });
            }

            var closeButton = Button("Close");
            closeButton.Margin = new Thickness(0, 22, 0, 0);
            closeButton.Click += (_, _) => detailsWindow.Close();

            root.Children.Add(closeButton);

            detailsWindow.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = root
            };

            detailsWindow.ShowDialog();
        }

        private Border Card(string title, TextBlock valueText)
        {
            valueText.Text = "--";
            valueText.Foreground = Brushes.White;
            valueText.FontSize = 20;
            valueText.FontWeight = FontWeights.Bold;
            valueText.Margin = new Thickness(0, 6, 0, 0);

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brush("#9FB3CC"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });

            stack.Children.Add(valueText);

            return new Border
            {
                Background = Brush("#0D1A2B"),
                BorderBrush = Brush("#263E5C"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 10, 0),
                Child = stack
            };
        }

        private Border CreateDetailRow(string label, object? value)
        {
            var panel = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new TextBlock
            {
                Text = label,
                Foreground = Brush("#9FB3CC"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var right = new TextBlock
            {
                Text = Convert.ToString(value) ?? "--",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(right, 1);

            panel.Children.Add(left);
            panel.Children.Add(right);

            return new Border
            {
                Background = Brush("#0D1A2B"),
                BorderBrush = Brush("#263E5C"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Child = panel
            };
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
            var s = FleetAnalyticsService.BuildSnapshot();

            _balanceText.Text = Money(s.CompanyBalance);
            _profitText.Text = Money(s.NetProfit);
            _weekText.Text = Money(s.WeekProfit);
            _monthText.Text = Money(s.MonthProfit);
            _rpmText.Text = Money2(s.ProfitPerMile);
            _scoreText.Text = s.AverageDriverScore.ToString("0.0");

            _profitText.Foreground = ProfitBrush(s.NetProfit);
            _weekText.Foreground = ProfitBrush(s.WeekProfit);
            _monthText.Foreground = ProfitBrush(s.MonthProfit);
            _rpmText.Foreground = ProfitBrush(s.ProfitPerMile);

            _trendGrid.ItemsSource =
                s.DailyTrends
                    .OrderByDescending(x => x.Date)
                    .Select(x => new
                    {
                        Date = x.Date.ToLocalTime().ToString("MM/dd"),
                        Revenue = Money(x.Revenue),
                        Expenses = Money(x.Expenses),
                        Profit = Money(x.Profit),
                        x.Transactions
                    })
                    .ToList();

            _expenseGrid.ItemsSource =
                s.ExpenseBreakdown
                    .Select(x => new
                    {
                        x.Category,
                        Amount = Money(x.Amount),
                        x.Count
                    })
                    .ToList();

            _revenueGrid.ItemsSource =
                s.RevenueBreakdown
                    .Select(x => new
                    {
                        x.Category,
                        Amount = Money(x.Amount),
                        x.Count
                    })
                    .ToList();

            _topDriversGrid.ItemsSource = RankRows(s.TopDrivers);
            _topTrucksGrid.ItemsSource = RankRows(s.TopTrucks);
            _problemTrucksGrid.ItemsSource = RankRows(s.ProblemTrucks);
            _contractsGrid.ItemsSource = RankRows(s.TopContracts);

            var fleetRows =
                FleetTruckApprovalService.BuildFleetTruckRows()
                    .Select(x =>
                    {
                        var isPending =
                            (x.ApprovalBadge ?? "")
                            .Equals("Pending", StringComparison.OrdinalIgnoreCase);

                        var isActive =
                            (x.ActiveLight ?? "")
                            .Contains("🟢");

                        return new
                        {
                            SortPending = isPending ? 0 : 1,
                            SortActive = isActive ? 0 : 1,
                            Active =
                                isPending
                                    ? "🟡"
                                    : isActive
                                        ? "🟢"
                                        : "🔴",
                            Status =
                                isPending
                                    ? "Pending Approval"
                                    : isActive
                                        ? "Active"
                                        : "Offline",
                            Badge = x.ApprovalBadge,
                            TruckNumber = x.TruckNumber,
                            Truck =
                                string.IsNullOrWhiteSpace(x.TruckName)
                                    ? x.MakeModel
                                    : x.TruckName,
                            Model = x.MakeModel,
                            Plate = x.PlateNumber,
                            Driver =
                                string.IsNullOrWhiteSpace(x.AssignedDriver)
                                    ? "Unassigned"
                                    : x.AssignedDriver,
                            Miles = x.OdometerMiles.ToString("N0"),
                            Fuel = x.FuelPercent.ToString("0.##") + "%",
                            Health = x.HealthPercent.ToString("0.##") + "%",
                            Damage = x.DamagePercent.ToString("0.##") + "%",
                            Location =
                                string.IsNullOrWhiteSpace(x.CurrentLocation)
                                    ? "--"
                                    : x.CurrentLocation
                        };
                    })
                    .OrderBy(x => x.SortPending)
                    .ThenBy(x => x.SortActive)
                    .ThenByDescending(x => x.Miles)
                    .ToList();

            var search =
                (_fleetTruckSearchBox.Text ?? "")
                .Trim()
                .ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(search))
            {
                fleetRows = fleetRows
                    .Where(x =>
                        (x.TruckNumber ?? "").ToLowerInvariant().Contains(search) ||
                        (x.Truck ?? "").ToLowerInvariant().Contains(search) ||
                        (x.Model ?? "").ToLowerInvariant().Contains(search) ||
                        (x.Plate ?? "").ToLowerInvariant().Contains(search) ||
                        (x.Driver ?? "").ToLowerInvariant().Contains(search) ||
                        (x.Status ?? "").ToLowerInvariant().Contains(search))
                    .ToList();
            }

            _fleetTruckSearchCount.Text = $"{fleetRows.Count:N0} trucks shown";
            _fleetTrucksGrid.ItemsSource = fleetRows;

            _footerText.Text =
                $"Generated {s.GeneratedUtc.ToLocalTime():g} • Drivers {s.DriversTracked} • Trucks {fleetRows.Count:N0} • Loads {s.TotalLoadsDelivered:N0} • Miles {s.TotalMiles:N0} • Contracts active/completed/failed {s.ActiveContracts}/{s.CompletedContracts}/{s.FailedContracts}";
        }

        private static IEnumerable RankRows(System.Collections.Generic.List<FleetAnalyticsRankRow> rows)
        {
            return rows.Select(x => new
            {
                x.Rank,
                x.Name,
                x.Secondary,
                Revenue = Money(x.Revenue),
                Cost = Money(x.Cost),
                Profit = Money(x.Profit),
                Miles = x.Miles.ToString("N0"),
                x.Loads,
                Score = x.Score.ToString("0.0")
            }).ToList();
        }

        private static string Money(decimal value) =>
            value.ToString("C0", CultureInfo.CurrentCulture);

        private static string Money2(decimal value) =>
            value.ToString("C2", CultureInfo.CurrentCulture);

        private static Brush ProfitBrush(decimal value)
        {
            if (value > 0)
                return Brush("#35B474");

            if (value < 0)
                return Brush("#EF4444");

            return Brushes.White;
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));
        }
    }
}