using OverWatchELD.Services.Economy;
using OverWatchELD.Services.Operations;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OverWatchELD.Views.Operations
{
    public sealed class OperationsCommandCenterWindow : Window
    {
        private readonly DataGrid _systemsGrid = new();
        private readonly DataGrid _lastSyncGrid = new();
        private readonly TextBlock _summaryText = new();

        public OperationsCommandCenterWindow()
        {
            Title = "OverWatch Operations Command Center";
            Width = 1240;
            Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07111F");

            Content = BuildLayout();
            Loaded += (_, _) => RefreshSystems();
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
                Text = "OverWatch Operations Command Center",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            };

            Grid.SetRow(title, 0);
            root.Children.Add(title);

            _summaryText.Foreground = Brush("#9FB3CC");
            _summaryText.FontSize = 14;
            _summaryText.Margin = new Thickness(0, 0, 0, 14);
            _summaryText.Text = "One place to sync dispatch, contracts, economy, payroll, fuel, maintenance, safety, and analytics.";

            Grid.SetRow(_summaryText, 1);
            root.Children.Add(_summaryText);

            SetupGrid(_systemsGrid);
            SetupGrid(_lastSyncGrid);

            var tabs = new TabControl
            {
                Background = Brush("#0D1A2B"),
                BorderBrush = Brush("#263E5C")
            };

            tabs.Items.Add(new TabItem { Header = "Connected Systems", Content = _systemsGrid });
            tabs.Items.Add(new TabItem { Header = "Last Sync Result", Content = _lastSyncGrid });

            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            var buttons = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            AddButton(buttons, "Sync Everything", (_, _) => RunFullSync());
            AddButton(buttons, "Fleet Economy", (_, _) => FleetEconomyIntegration.OpenEconomyWindow(this));
            AddButton(buttons, "Payroll", (_, _) => FleetEconomyIntegration.OpenRealDriverEconomyWindow(this));
            AddButton(buttons, "Profit / Leaderboards", (_, _) => FleetEconomyIntegration.OpenTruckProfitabilityLeaderboardsWindow(this));
            AddButton(buttons, "Driver Scores", (_, _) => FleetEconomyIntegration.OpenDriverSafetyPerformanceWindow(this));
            AddButton(buttons, "Fuel / Maintenance", (_, _) => FleetEconomyIntegration.OpenFuelMaintenanceAutomationWindow(this));
            AddButton(buttons, "Contracts", (_, _) => FleetEconomyIntegration.OpenDispatchContractsWindow(this));
            AddButton(buttons, "Fleet Analytics", (_, _) => FleetEconomyIntegration.OpenFleetAnalyticsDashboardWindow(this));
            AddButton(buttons, "Refresh", (_, _) => RefreshSystems());
            AddButton(buttons, "Close", (_, _) => Close());

            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            return root;
        }

        private void RunFullSync()
        {
            var result = OperationsOrchestratorService.RunFullSync();

            _summaryText.Text =
                $"{result.Summary} Started {result.StartedUtc.ToLocalTime():g}, completed {result.CompletedUtc.ToLocalTime():g}.";

            _lastSyncGrid.ItemsSource = result.Completed
                .Select(x => new { Result = "Complete", Operation = x })
                .Concat(result.Warnings.Select(x => new { Result = "Warning", Operation = x }))
                .Concat(result.Errors.Select(x => new { Result = "Error", Operation = x }))
                .ToList();

            RefreshSystems();

            MessageBox.Show(
                result.Summary,
                "Operations Sync",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void RefreshSystems()
        {
            _systemsGrid.ItemsSource = OperationsOrchestratorService.BuildDashboardRows()
                .Select(x => new
                {
                    x.System,
                    x.Purpose,
                    x.Status,
                    LastChecked = x.LastChecked.ToString("g")
                })
                .ToList();
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

        private void AddButton(Panel panel, string text, RoutedEventHandler click)
        {
            var button = new Button
            {
                Content = text,
                Height = 38,
                MinWidth = 118,
                Margin = new Thickness(6, 0, 0, 8),
                Background = Brush("#163B65"),
                BorderBrush = Brush("#4A91D0"),
                Foreground = Brushes.White,
                Padding = new Thickness(12, 4, 12, 4)
            };

            button.Click += click;
            panel.Children.Add(button);
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
