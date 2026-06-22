using OverWatchELD.Models.Dispatch;
using OverWatchELD.Services.Dispatch;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OverWatchELD.Views.Dispatch
{
    public sealed class DispatchContractsWindow : Window
    {
        private readonly DataGrid _contractsGrid = new();
        private readonly DataGrid _eventsGrid = new();
        private readonly TextBlock _summaryText = new();

        public DispatchContractsWindow()
        {
            Title = "Dispatch Contracts";
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
                Text = "Dispatch Contracts",
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

            SetupGrid(_contractsGrid);
            SetupGrid(_eventsGrid);

            var tabs = new TabControl
            {
                Background = Brush("#0D1A2B"),
                BorderBrush = Brush("#263E5C")
            };

            tabs.Items.Add(new TabItem { Header = "Contracts", Content = _contractsGrid });
            tabs.Items.Add(new TabItem { Header = "Contract Events", Content = _eventsGrid });

            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var add = Button("New Contract");
            add.Click += (_, _) =>
            {
                var win = new DispatchContractEditWindow
                {
                    Owner = this
                };

                if (win.ShowDialog() == true && win.Contract != null)
                {
                    DispatchContractService.CreateContract(win.Contract);
                    Refresh();
                }
            };

            var seed = Button("Seed Samples");
            seed.Click += (_, _) =>
            {
                DispatchContractService.SeedSampleContracts();
                Refresh();
            };

            var exportAts = Button("Export Next Load To ATS");
            exportAts.Click += (_, _) =>
            {
                var contractId = SelectedContractId();

                if (string.IsNullOrWhiteSpace(contractId))
                {
                    MessageBox.Show("Select an active contract first.", "Dispatch Contracts");
                    return;
                }

                var result = DispatchContractAtsIntegrationService.ExportNextContractLoadToAts(contractId);
                Refresh();

                var msg =
                    $"{(result.Success ? "ATS export/injection complete." : "ATS export/injection failed.")}\n\n" +
                    $"Contract: {result.ContractNumber}\n" +
                    $"Load: {result.LoadNumber}\n" +
                    $"Message: {result.Message}\n\n" +
                    $"Save: {result.SavePath}\n" +
                    $"Backup: {result.BackupPath}\n" +
                    $"Unit: {result.InjectedUnitId}";

                if (result.Warnings.Count > 0)
                    msg += "\n\nWarnings:\n- " + string.Join("\n- ", result.Warnings);

                MessageBox.Show(
                    msg,
                    "Contract → ATS",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            };

            var syncDelivered = Button("Sync Delivered Contract Loads");
            syncDelivered.Click += (_, _) =>
            {
                DispatchContractAtsIntegrationService.SyncDeliveredContractLoads();
                Refresh();
            };

            var markLoad = Button("Manual Load Progress");
            markLoad.Click += (_, _) =>
            {
                var contractId = SelectedContractId();

                if (!string.IsNullOrWhiteSpace(contractId))
                {
                    DispatchContractService.RecordLoadDeliveredForContract(contractId);
                    Refresh();
                }
            };

            var cancel = Button("Cancel Selected");
            cancel.Click += (_, _) =>
            {
                var contractId = SelectedContractId();

                if (!string.IsNullOrWhiteSpace(contractId))
                {
                    DispatchContractService.CancelContract(contractId);
                    Refresh();
                }
            };

            var refresh = Button("Refresh");
            refresh.Click += (_, _) => Refresh();

            var close = Button("Close");
            close.Click += (_, _) => Close();

            buttons.Children.Add(add);
            buttons.Children.Add(seed);
            buttons.Children.Add(exportAts);
            buttons.Children.Add(syncDelivered);
            buttons.Children.Add(markLoad);
            buttons.Children.Add(cancel);
            buttons.Children.Add(refresh);
            buttons.Children.Add(close);

            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            return root;
        }

        private string SelectedContractId()
        {
            if (_contractsGrid.SelectedItem == null)
                return "";

            return (_contractsGrid.SelectedItem
                .GetType()
                .GetProperty("Id")
                ?.GetValue(_contractsGrid.SelectedItem)
                ?.ToString() ?? "").Trim();
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
            DispatchContractAtsIntegrationService.SyncDeliveredContractLoads();

            var contracts = DispatchContractService.LoadContracts();
            var events = DispatchContractService.LoadEvents();

            var active = contracts.Count(x => x.Status.Equals("Active", StringComparison.OrdinalIgnoreCase));
            var completed = contracts.Count(x => x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
            var failed = contracts.Count(x => x.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
            var revenue = contracts.Sum(x => x.EstimatedRevenue);

            _summaryText.Text =
                $"Contracts: {contracts.Count}   Active: {active}   Completed: {completed}   Failed: {failed}   " +
                $"Estimated Revenue: {Money(revenue)}";

            _contractsGrid.ItemsSource = contracts
                .OrderByDescending(x => x.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.DueUtc)
                .Select(x => new
                {
                    x.Id,
                    Contract = x.ContractNumber,
                    Customer = x.CustomerName,
                    Type = x.ContractType,
                    Lane = $"{x.OriginCity}, {x.OriginState} → {x.DestinationCity}, {x.DestinationState}",
                    x.Cargo,
                    Trailer = x.TrailerType,
                    Progress = $"{x.CompletedLoads}/{x.RequiredLoads} ({x.ProgressPercent:0.#}%)",
                    Revenue = Money(x.EstimatedRevenue),
                    Bonus = Money(x.BonusAmount),
                    Penalty = Money(x.PenaltyAmount),
                    Due = x.DueUtc.ToLocalTime().ToString("g"),
                    x.Status,
                    Driver = x.AssignedDriver,
                    Truck = FirstNonEmpty(x.AssignedTruckNumber, x.AssignedTruckName)
                })
                .ToList();

            _eventsGrid.ItemsSource = events
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => new
                {
                    Date = x.CreatedUtc.ToLocalTime().ToString("g"),
                    Contract = x.ContractNumber,
                    Type = x.EventType,
                    x.Message,
                    Load = x.LoadNumber,
                    Amount = Money(x.Amount)
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
