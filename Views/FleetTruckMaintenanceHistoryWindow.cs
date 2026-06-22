using OverWatchELD.Models;
using OverWatchELD.Models.Fleet;
using OverWatchELD.Stores;
using System;
using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OverWatchELD.Views
{
    public sealed class FleetTruckMaintenanceHistoryWindow : Window
    {
        public FleetTruckMaintenanceHistoryWindow(FleetCommandTruck fleetTruck)
        {
            Title = "Truck Maintenance History";
            Width = 900;
            Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(7, 17, 31));

            var maintenanceTruck = FindMaintenanceTruck(fleetTruck);

            Content = BuildContent(fleetTruck, maintenanceTruck);
        }

        private static VtcMaintenanceTruck? FindMaintenanceTruck(FleetCommandTruck fleetTruck)
        {
            try
            {
                var state = VtcMaintenanceStore.Load();

                return state.Trucks.FirstOrDefault(t =>
                    Same(t.TruckId, fleetTruck.Id) ||
                    Same(t.UnitNumber, fleetTruck.TruckNumber) ||
                    Same(t.TruckName, fleetTruck.TruckName) ||
                    Same(t.PlateNumber, fleetTruck.PlateNumber));
            }
            catch
            {
                return null;
            }
        }

        private UIElement BuildContent(FleetCommandTruck fleetTruck, VtcMaintenanceTruck? maintenanceTruck)
        {
            var root = new Grid { Margin = new Thickness(18) };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

            header.Children.Add(new TextBlock
            {
                Text = $"Truck {FirstNonEmpty(fleetTruck.TruckNumber, maintenanceTruck?.UnitNumber, "Unknown")}",
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.Bold
            });

            header.Children.Add(new TextBlock
            {
                Text = $"{FirstNonEmpty(fleetTruck.TruckName, maintenanceTruck?.TruckName, "No truck name")}  •  Plate: {FirstNonEmpty(fleetTruck.PlateNumber, maintenanceTruck?.PlateNumber, "--")}  •  Driver: {FirstNonEmpty(fleetTruck.AssignedDriver, maintenanceTruck?.AssignedDriver, "--")}",
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                FontSize = 14,
                Margin = new Thickness(0, 6, 0, 0)
            });

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var tabs = new TabControl
            {
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 31)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 62, 92))
            };

            tabs.ItemContainerStyle = new Style(typeof(TabItem))
            {
                Setters =
    {
        new Setter(Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(13, 26, 43))),

        new Setter(Control.ForegroundProperty,
            Brushes.White),

        new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(38, 62, 92))),

        new Setter(Control.PaddingProperty,
            new Thickness(18,8,18,8)),

        new Setter(Control.FontWeightProperty,
            FontWeights.SemiBold),

        new Setter(Control.MarginProperty,
            new Thickness(0,0,2,0))
    }
            };

            tabs.ItemContainerStyle.Triggers.Add(new Trigger
            {
                Property = TabItem.IsSelectedProperty,
                Value = true,
                Setters =
    {
        new Setter(Control.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(22, 59, 101))),

        new Setter(Control.ForegroundProperty,
            Brushes.White),

        new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(74, 145, 208)))
    }
            });

            tabs.Resources[typeof(TabPanel)] = new Style(typeof(TabPanel))
            {
                Setters =
    {
        new Setter(Panel.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(7, 17, 31)))
    }
            };

            if (maintenanceTruck == null)
            {
                tabs.Items.Add(new TabItem
                {
                    Header = "Maintenance History",
                    Content = MakeEmptyText("No VTC Maintenance history found for this fleet truck yet.")
                });
            }
            else
            {
                tabs.Items.Add(new TabItem
                {
                    Header = "Service History",
                    Content = MakeServiceGrid(maintenanceTruck)
                });

                tabs.Items.Add(new TabItem
                {
                    Header = "Damage / Malfunctions",
                    Content = MakeDamageGrid(maintenanceTruck)
                });

                tabs.Items.Add(new TabItem
                {
                    Header = "Current Status",
                    Content = MakeStatusPanel(maintenanceTruck)
                });
            }

            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            var closeButton = new Button
            {
                Content = "Close",
                Width = 120,
                Height = 38,
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(22, 59, 101)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(74, 145, 208))
            };

            closeButton.Click += (_, _) => Close();

            Grid.SetRow(closeButton, 2);
            root.Children.Add(closeButton);

            return root;
        }

        private static DataGrid MakeServiceGrid(VtcMaintenanceTruck truck)
        {
            var rows = truck.ServiceHistory
                .OrderByDescending(x => x.CompletedUtc)
                .Select(x => new
                {
                    Date = x.CompletedUtc.ToLocalTime().ToString("g"),
                    Type = x.ServiceType,
                    Odometer = x.OdometerMiles.ToString("N0"),
                    CompletedBy = x.CompletedBy,
                    Notes = x.Notes
                })
                .ToList();

            return MakeGrid(rows);
        }

        private static DataGrid MakeDamageGrid(VtcMaintenanceTruck truck)
        {
            var rows = truck.DamageReports
                .OrderByDescending(x => x.ReportedUtc)
                .Select(x => new
                {
                    Date = x.ReportedUtc.ToLocalTime().ToString("g"),
                    Severity = x.Severity,
                    Status = x.Resolved ? "Resolved" : "Open",
                    ReportedBy = x.ReportedBy,
                    Notes = x.Notes
                })
                .ToList();

            return MakeGrid(rows);
        }

        private static DataGrid MakeGrid(IEnumerable rows)
        {
            var grid = new DataGrid
            {
                ItemsSource = rows,
                AutoGenerateColumns = true,
                IsReadOnly = true,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,

                Background = new SolidColorBrush(Color.FromRgb(7, 17, 31)),
                Foreground = Brushes.White,

                RowBackground = new SolidColorBrush(Color.FromRgb(11, 22, 38)),
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(16, 30, 48)),

                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 62, 92)),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,

                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(38, 62, 92)),
                VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(25, 40, 60)),

                SelectionUnit = DataGridSelectionUnit.FullRow
            };

            grid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty,
                        new SolidColorBrush(Color.FromRgb(13, 26, 43))),

                    new Setter(Control.ForegroundProperty,
                        Brushes.White),

                    new Setter(Control.BorderBrushProperty,
                        new SolidColorBrush(Color.FromRgb(38, 62, 92))),

                    new Setter(Control.BorderThicknessProperty,
                        new Thickness(0,0,0,1)),

                    new Setter(Control.FontWeightProperty,
                        FontWeights.SemiBold),

                    new Setter(Control.PaddingProperty,
                        new Thickness(10,8,10,8))
                }
            };

            grid.CellStyle = new Style(typeof(DataGridCell))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty,
                        Brushes.Transparent),

                    new Setter(Control.ForegroundProperty,
                        Brushes.White),

                    new Setter(Control.BorderBrushProperty,
                        new SolidColorBrush(Color.FromRgb(25, 40, 60))),

                    new Setter(Control.PaddingProperty,
                        new Thickness(8,6,8,6))
                }
            };

            grid.CellStyle.Triggers.Add(new Trigger
            {
                Property = DataGridCell.IsSelectedProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty,
                        new SolidColorBrush(Color.FromRgb(31, 122, 77))),

                    new Setter(Control.ForegroundProperty,
                        Brushes.White)
                }
            });

            return grid;
        }

        private static UIElement MakeStatusPanel(VtcMaintenanceTruck truck)
        {
            var panel = new StackPanel { Margin = new Thickness(16) };

            AddStatus(panel, "Unit Number", truck.UnitNumber);
            AddStatus(panel, "Truck Name", truck.TruckName);
            AddStatus(panel, "Plate", truck.PlateNumber);
            AddStatus(panel, "Assigned Driver", truck.AssignedDriver);
            AddStatus(panel, "Location", truck.Location);
            AddStatus(panel, "Odometer", truck.OdometerMiles.ToString("N0"));
            AddStatus(panel, "Fuel", $"{truck.FuelPercent:0.#}%");
            AddStatus(panel, "Condition", $"{truck.ConditionPercent:0.#}%");
            AddStatus(panel, "Out Of Service", truck.OutOfService ? "Yes" : "No");
            AddStatus(panel, "Current Issue", FirstNonEmpty(truck.CurrentIssue, "--"));
            AddStatus(panel, "Issue Severity", FirstNonEmpty(truck.CurrentIssueSeverity, "--"));
            AddStatus(panel, "Last Service", truck.LastServiceUtc?.ToLocalTime().ToString("g") ?? "--");
            AddStatus(panel, "Last Inspection", truck.LastInspectionUtc?.ToLocalTime().ToString("g") ?? "--");
            AddStatus(panel, "DOT Expiration", truck.DotExpirationUtc?.ToLocalTime().ToString("d") ?? "--");

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };
        }

        private static UIElement MakeEmptyText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 16,
                Margin = new Thickness(18),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static void AddStatus(StackPanel panel, string label, string value)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"{label}: {value}",
                Foreground = Brushes.White,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
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