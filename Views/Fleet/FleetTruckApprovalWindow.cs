using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OverWatchELD.Views.Fleet
{
    public sealed class FleetTruckApprovalWindow : Window
    {
        private readonly DataGrid _pendingGrid = new();
        private readonly TextBlock _summaryText = new();
        private readonly TextBox _managerTruckNumberBox = new();

        public FleetTruckApprovalWindow()
        {
            Title = "Fleet Truck Approvals";
            Width = 1180;
            Height = 760;
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
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Fleet Truck Approvals",
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

            var assignPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };

            assignPanel.Children.Add(new TextBlock
            {
                Text = "Manager Assigned Truck #:",
                Foreground = Brush("#9FB3CC"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            _managerTruckNumberBox.Width = 180;
            _managerTruckNumberBox.Height = 34;
            _managerTruckNumberBox.Background = Brush("#0D1A2B");
            _managerTruckNumberBox.Foreground = Brushes.White;
            _managerTruckNumberBox.BorderBrush = Brush("#263E5C");
            _managerTruckNumberBox.Padding = new Thickness(8, 4, 8, 4);

            assignPanel.Children.Add(_managerTruckNumberBox);

            assignPanel.Children.Add(new TextBlock
            {
                Text = "Required. Once approved, this number is locked and cannot be reused.",
                Foreground = Brush("#EF4444"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            });

            Grid.SetRow(assignPanel, 2);
            root.Children.Add(assignPanel);

            SetupGrid(_pendingGrid);

            _pendingGrid.SelectionChanged += (_, _) => FillSelectedTruckNumber();

            Grid.SetRow(_pendingGrid, 3);
            root.Children.Add(_pendingGrid);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            AddButton(buttons, "Approve Selected", (_, _) => ApproveSelected());
            AddButton(buttons, "Deny Selected", (_, _) => DenySelected());
            AddButton(buttons, "Refresh", (_, _) => Refresh());
            AddButton(buttons, "Close", (_, _) => Close());

            Grid.SetRow(buttons, 4);
            root.Children.Add(buttons);

            return root;
        }

        private string SelectedId()
        {
            if (_pendingGrid.SelectedItem == null)
                return "";

            return _pendingGrid.SelectedItem.GetType()
                .GetProperty("Id")
                ?.GetValue(_pendingGrid.SelectedItem)
                ?.ToString() ?? "";
        }

        private string SelectedTruckNumber()
        {
            if (_pendingGrid.SelectedItem == null)
                return "";

            return _pendingGrid.SelectedItem.GetType()
                .GetProperty("RequestedTruckNumber")
                ?.GetValue(_pendingGrid.SelectedItem)
                ?.ToString() ?? "";
        }

        private void FillSelectedTruckNumber()
        {
            var selected = SelectedTruckNumber();

            if (!string.IsNullOrWhiteSpace(selected))
            {
                _managerTruckNumberBox.Text =
                    FleetTruckNumberLockStore.Normalize(selected);
            }
        }

        private async void ApproveSelected()
        {
            var id = SelectedId();

            if (string.IsNullOrWhiteSpace(id))
            {
                MessageBox.Show(
                    "Select a pending truck first.",
                    "Fleet Approvals");

                return;
            }

            var assignedNumber =
                FleetTruckNumberLockStore.Normalize(_managerTruckNumberBox.Text);

            if (string.IsNullOrWhiteSpace(assignedNumber))
            {
                MessageBox.Show(
                    "Manager must assign a truck number before approval.",
                    "Fleet Approvals",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                FleetTruckApprovalService.Approve(
                    id,
                    assignedNumber,
                    EldDriverIdentityResolver.DriverName());

                await PostDiscordApprovalAsync(assignedNumber);

                MessageBox.Show(
                    $"Truck approved and locked to truck number {assignedNumber}.\n\nThis number cannot be reused.",
                    "Fleet Approvals",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                _managerTruckNumberBox.Text = "";

                Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Fleet Approvals",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async Task PostDiscordApprovalAsync(string assignedNumber)
        {
            try
            {
                var selected = _pendingGrid.SelectedItem;

                if (selected == null)
                    return;

                var driver =
                    selected.GetType()
                        .GetProperty("Driver")
                        ?.GetValue(selected)
                        ?.ToString() ?? "";

                var truck =
                    selected.GetType()
                        .GetProperty("Truck")
                        ?.GetValue(selected)
                        ?.ToString() ?? "";

                var makeModel =
                    selected.GetType()
                        .GetProperty("MakeModel")
                        ?.GetValue(selected)
                        ?.ToString() ?? "";

                var plate =
                    selected.GetType()
                        .GetProperty("PlateNumber")
                        ?.GetValue(selected)
                        ?.ToString()
                    ??
                    selected.GetType()
                        .GetProperty("Plate")
                        ?.GetValue(selected)
                        ?.ToString()
                    ?? "";

                var miles =
                    selected.GetType()
                        .GetProperty("Miles")
                        ?.GetValue(selected)
                        ?.ToString() ?? "";

                var cfg = VtcConfigService.Load();

                using var http = new HttpClient();

                var payload = new
                {
                    guildId = cfg.GuildId,
                    truckNumber = assignedNumber,
                    driverName = driver,
                    truckName = truck,
                    model = makeModel,
                    plate = plate,
                    mileage = miles
                };

                var json =
                    JsonSerializer.Serialize(payload);

                using var content =
                    new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json");

                var response =
                    await http.PostAsync(
                        $"{cfg.BotApiBaseUrl.TrimEnd('/')}/api/api/fleet/truck-approved",
                        content);

                var body =
                    await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        $"Discord fleet post failed.\n\nStatus: {(int)response.StatusCode}\n\nBody:\n{body}",
                        "Discord Fleet Post",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Discord Fleet Post",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DenySelected()
        {
            var id = SelectedId();

            if (string.IsNullOrWhiteSpace(id))
            {
                MessageBox.Show(
                    "Select a pending truck first.",
                    "Fleet Approvals");

                return;
            }

            FleetTruckApprovalService.Deny(
                id,
                EldDriverIdentityResolver.DriverName());

            _managerTruckNumberBox.Text = "";

            Refresh();
        }

        private void Refresh()
        {
            var locked = FleetTruckNumberLockStore.Load()
                .Select(x => FleetTruckNumberLockStore.Normalize(x.TruckNumber))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rows = PendingFleetTruckApprovalStore.Load()
                .OrderBy(x => x.Status == "Pending" ? 0 : 1)
                .ThenByDescending(x => x.UpdatedUtc)
                .Select(x =>
                {
                    var requested =
                        FleetTruckNumberLockStore.Normalize(x.TruckNumber);

                    return new
                    {
                        x.Id,
                        Badge = x.Status,
                        RequestedTruckNumber = requested,
                        NumberLocked = locked.Contains(requested)
                            ? "LOCKED"
                            : "",
                        Truck = FirstNonEmpty(x.TruckName, requested),
                        x.MakeModel,
                        x.PlateNumber,
                        Driver = x.AssignedDriver,
                        Miles = x.OdometerMiles.ToString("N0"),
                        Fuel = x.FuelPercent.ToString("0.##") + "%",
                        Health = x.HealthPercent.ToString("0.##") + "%",
                        Damage = x.DamagePercent.ToString("0.##") + "%",
                        x.CurrentLocation,
                        Submitted = x.CreatedUtc.ToLocalTime().ToString("g"),
                        Reviewed = x.ReviewedUtc?.ToLocalTime().ToString("g") ?? "",
                        x.ReviewedBy,
                        x.Notes
                    };
                })
                .ToList();

            _summaryText.Text =
                $"Pending: {PendingFleetTruckApprovalStore.PendingCount()}   Total Requests: {rows.Count}   Locked Truck Numbers: {locked.Count}";

            _pendingGrid.ItemsSource = rows;
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

        private void AddButton(
            Panel panel,
            string text,
            RoutedEventHandler handler)
        {
            var button = new Button
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

            button.Click += handler;

            panel.Children.Add(button);
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

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));
        }
    }
}