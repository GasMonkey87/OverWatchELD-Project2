using OverWatchELD.Models.Economy;
using OverWatchELD.Services.Economy;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;

namespace OverWatchELD.Views.Economy
{
    public sealed class FleetEconomyWindow : Window
    {
        private readonly TextBlock _balanceText = new();
        private readonly TextBlock _todayText = new();
        private readonly TextBlock _weekText = new();
        private readonly TextBlock _monthText = new();
        private readonly TextBlock _lifetimeText = new();
        private readonly DataGrid _grid = new();

        public FleetEconomyWindow()
        {
            Title = "Fleet Economy";
            Width = 1120;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07111F");

            Content = BuildLayout();

            Loaded += (_, _) => Refresh();
        }

        private UIElement BuildLayout()
        {
            var root = new Grid
            {
                Margin = new Thickness(18)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Fleet Economy Center",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var cards = new UniformGrid
            {
                Columns = 5,
                Margin = new Thickness(0, 0, 0, 16)
            };

            cards.Children.Add(Card("Company Balance", _balanceText));
            cards.Children.Add(Card("Today", _todayText));
            cards.Children.Add(Card("This Week", _weekText));
            cards.Children.Add(Card("This Month", _monthText));
            cards.Children.Add(Card("Lifetime Profit", _lifetimeText));

            Grid.SetRow(cards, 1);
            root.Children.Add(cards);

            _grid.AutoGenerateColumns = true;
            _grid.IsReadOnly = true;
            _grid.CanUserAddRows = false;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.Background = Brush("#0D1A2B");
            _grid.Foreground = Brushes.White;
            _grid.RowBackground = Brush("#0B1626");
            _grid.AlternatingRowBackground = Brush("#102038");
            _grid.BorderBrush = Brush("#263E5C");
            _grid.HorizontalGridLinesBrush = Brush("#263E5C");
            _grid.VerticalGridLinesBrush = Brush("#1A2D45");
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            _grid.SelectionUnit = DataGridSelectionUnit.FullRow;

            _grid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
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

            _grid.CellStyle = new Style(typeof(DataGridCell))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, Brushes.Transparent),
                    new Setter(Control.ForegroundProperty, Brushes.White),
                    new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6))
                }
            };

            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var sync = Button("Sync Delivered Loads");
            sync.Click += (_, _) =>
            {
                FleetEconomyService.SyncDeliveredDispatchJobs();
                Refresh();
                MessageBox.Show("Delivered load payouts synced.", "Fleet Economy");
            };

            var garage = Button("Post Daily Garage Income");
            garage.Click += (_, _) =>
            {
                GarageEconomyService.PostDailyGarageIncome();
                Refresh();
                MessageBox.Show("Garage income posted.", "Fleet Economy");
            };

            var refresh = Button("Refresh");
            refresh.Click += (_, _) => Refresh();

            var close = Button("Close");
            close.Click += (_, _) => Close();

            buttons.Children.Add(sync);
            buttons.Children.Add(garage);
            buttons.Children.Add(refresh);
            buttons.Children.Add(close);

            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            return root;
        }

        private Border Card(string title, TextBlock valueText)
        {
            valueText.Text = "$0";
            valueText.Foreground = Brushes.White;
            valueText.FontSize = 22;
            valueText.FontWeight = FontWeights.Bold;
            valueText.Margin = new Thickness(0, 6, 0, 0);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brush("#9FB3CC"),
                FontSize = 13,
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
            FleetEconomyService.SyncDeliveredDispatchJobs();

            var s = FleetEconomyService.GetSummary();

            _balanceText.Text = Money(s.Balance);
            _todayText.Text = Money(s.TodayProfit);
            _weekText.Text = Money(s.WeekProfit);
            _monthText.Text = Money(s.MonthProfit);
            _lifetimeText.Text = Money(s.LifetimeProfit);

            _todayText.Foreground = ProfitBrush(s.TodayProfit);
            _weekText.Foreground = ProfitBrush(s.WeekProfit);
            _monthText.Foreground = ProfitBrush(s.MonthProfit);
            _lifetimeText.Foreground = ProfitBrush(s.LifetimeProfit);

            _grid.ItemsSource = EconomyStore.LoadTransactions()
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => new
                {
                    Date = x.CreatedUtc.ToLocalTime().ToString("g"),
                    x.Type,
                    x.Category,
                    Amount = Money(x.Amount),
                    Balance = Money(x.BalanceAfter),
                    Driver = x.DriverName,
                    Truck = FirstNonEmpty(x.TruckNumber, x.TruckName),
                    Load = x.LoadNumber,
                    x.Description
                })
                .ToList();
        }

        private static string Money(decimal value)
        {
            return value.ToString("C0", CultureInfo.CurrentCulture);
        }

        private static Brush ProfitBrush(decimal value)
        {
            if (value > 0) return Brush("#35B474");
            if (value < 0) return Brush("#EF4444");
            return Brushes.White;
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
