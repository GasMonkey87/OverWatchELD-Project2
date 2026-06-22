using OverWatchELD.Services.Performance;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OverWatchELD.Views.Performance
{
    public sealed class DriverSafetyPerformanceWindow : Window
    {
        private readonly DataGrid _leaderboardGrid = new();
        private readonly DataGrid _eventsGrid = new();
        private readonly TextBlock _summaryText = new();

        public DriverSafetyPerformanceWindow()
        {
            Title = "Driver Safety + Performance Scoring";
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
                Text = "Driver Safety + Performance Scoring",
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

            SetupGrid(_leaderboardGrid);
            SetupGrid(_eventsGrid);

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

            tabs.Items.Add(new TabItem { Header = "Safety Leaderboard", Content = _leaderboardGrid });
            tabs.Items.Add(new TabItem { Header = "Performance Events", Content = _eventsGrid });

            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var refresh = Button("Refresh");
            refresh.Click += (_, _) => Refresh();

            var close = Button("Close");
            close.Click += (_, _) => Close();

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
            var rows = DriverSafetyPerformanceService.BuildLeaderboard();
            var events = DriverPerformanceEventStore.Load();

            _summaryText.Text =
                $"Drivers: {rows.Count}   Avg Overall: {Avg(rows.Select(x => x.OverallScore)):0.0}   " +
                $"Avg Safety: {Avg(rows.Select(x => x.SafetyScore)):0.0}   Avg Performance: {Avg(rows.Select(x => x.PerformanceScore)):0.0}   " +
                $"Events: {events.Count:N0}";

            _leaderboardGrid.ItemsSource = rows.Select(x => new
            {
                x.Rank,
                Driver = x.DriverName,
                x.Grade,
                Overall = x.OverallScore.ToString("0.0"),
                Safety = x.SafetyScore.ToString("0.0"),
                Performance = x.PerformanceScore.ToString("0.0"),
                Economy = x.EconomyScore.ToString("0.0"),
                Loads = x.LoadsDelivered,
                Miles = x.MilesDriven.ToString("N0"),
                Revenue = Money(x.GrossRevenue),
                Profit = Money(x.CompanyProfit),
                Speeding = x.SpeedingEvents,
                Braking = x.HarshBrakeEvents,
                Idle = x.IdleEvents,
                Damage = x.DamageEvents,
                Late = x.LateDeliveries,
                Truck = FirstNonEmpty(x.TruckNumber, x.TruckName),
                LastActivity = x.LastActivityUtc?.ToLocalTime().ToString("g") ?? ""
            }).ToList();

            _eventsGrid.ItemsSource = events
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => new
                {
                    Date = x.CreatedUtc.ToLocalTime().ToString("g"),
                    Driver = x.DriverName,
                    Truck = FirstNonEmpty(x.TruckNumber, x.TruckName),
                    Type = x.EventType,
                    x.Severity,
                    Value = x.Value.ToString("0.##"),
                    x.Description,
                    x.Source
                })
                .ToList();
        }

        private static double Avg(IEnumerable<double> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? 0 : list.Average();
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
