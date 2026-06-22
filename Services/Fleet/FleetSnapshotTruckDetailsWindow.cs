using OverWatchELD.Models;
using OverWatchELD.Stores;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Views.Fleet
{
    public sealed class FleetSnapshotTruckDetailsWindow : Window
    {
        private readonly string _truckId;
        private readonly string _truckName;
        private readonly string _driver;

        public FleetSnapshotTruckDetailsWindow(
            string truckId,
            string truckName,
            string driver)
        {
            _truckId = truckId ?? "";
            _truckName = truckName ?? "";
            _driver = driver ?? "";

            Title = "Truck Details";
            Width = 720;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07101F");

            Content = Build();
        }

        private UIElement Build()
        {
            var state = VtcMaintenanceStore.Load();

            var truck = state?.Trucks?.FirstOrDefault(t =>
                Same(t.TruckId, _truckId) ||
                Same(t.UnitNumber, _truckId) ||
                Same(t.TruckName, _truckName) ||
                Same(t.AssignedDriver, _driver));

            var root = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = BuildContent(truck)
            };

            return root;
        }

        private UIElement BuildContent(VtcMaintenanceTruck? truck)
        {
            var root = new StackPanel
            {
                Margin = new Thickness(20)
            };

            root.Children.Add(new TextBlock
            {
                Text = $"Truck Details - {_truckName}",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold
            });

            root.Children.Add(new TextBlock
            {
                Text = $"Driver: {Safe(_driver)}",
                Foreground = Brush("#9FB4D0"),
                FontSize = 15,
                Margin = new Thickness(0, 4, 0, 18)
            });

            if (truck == null)
            {
                root.Children.Add(Card(
                    "No Maintenance Record",
                    "This truck has no VTC Maintenance record yet.",
                    "#F59E0B"));

                return root;
            }

            root.Children.Add(Card(
                "Status",
                BuildStatusText(truck),
                StatusColor(truck)));

            root.Children.Add(Card(
                "Needs Attention",
                NeedsAttentionText(truck),
                NeedsAttentionColor(truck)));

            root.Children.Add(Card(
                "Inspection",
                InspectionText(truck),
                InspectionColor(truck)));

            root.Children.Add(Card(
                "Service Due",
                ServiceText(truck),
                ServiceColor(truck)));

            root.Children.Add(Card(
                "Malfunctions",
                MalfunctionText(truck),
                MalfunctionColor(truck)));

            root.Children.Add(Card(
                "Truck Info",
                TruckInfoText(truck),
                "#38BDF8"));

            root.Children.Add(new TextBlock
            {
                Text = "Recent Maintenance / Request Log",
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 18, 0, 10)
            });

            var records = truck.ServiceHistory?
                .OrderByDescending(x => x.CompletedUtc)
                .Take(12)
                .ToList();

            if (records == null || records.Count == 0)
            {
                root.Children.Add(Card(
                    "No Logs",
                    "No service, inspection, malfunction, or request logs found.",
                    "#64748B"));
            }
            else
            {
                foreach (var r in records)
                {
                    root.Children.Add(Card(
                        r.ServiceType ?? "Maintenance Log",
                        $"By: {Safe(r.CompletedBy)}\n" +
                        $"Odometer: {r.OdometerMiles:0}\n" +
                        $"Notes: {Safe(r.Notes)}",
                        "#60A5FA"));
                }
            }

            return root;
        }

        private static string BuildStatusText(VtcMaintenanceTruck t)
        {
            if (t.OutOfService)
                return "Out Of Service";

            if (!string.IsNullOrWhiteSpace(t.CurrentIssue))
                return "Malfunction Active";

            if (IsServiceDue(t))
                return "Service Due";

            if (IsInspectionDue(t))
                return "Inspection Due / Expiring Soon";

            if (t.ConditionPercent <= 65)
                return "Critical Condition";

            return "Healthy";
        }

        private static string NeedsAttentionText(VtcMaintenanceTruck t)
        {
            var attention =
                t.OutOfService ||
                !string.IsNullOrWhiteSpace(t.CurrentIssue) ||
                IsServiceDue(t) ||
                IsInspectionDue(t) ||
                t.ConditionPercent <= 65;

            return attention
                ? "YES - This truck needs attention."
                : "No attention required.";
        }

        private static string InspectionText(VtcMaintenanceTruck t)
        {
            var last =
                t.LastInspectionUtc?.ToLocalTime().ToString("g")
                ?? "Unknown";

            var due =
                t.DotExpirationUtc?.ToLocalTime().ToString("g")
                ?? "Unknown";

            return
                $"Last Inspection: {last}\n" +
                $"DOT Expiration: {due}";
        }

        private static string ServiceText(VtcMaintenanceTruck t)
        {
            var last =
                t.LastServiceUtc?.ToLocalTime().ToString("g")
                ?? "Unknown";

            return
                $"Last Service: {last}\n" +
                $"Odometer: {t.OdometerMiles:0}";
        }

        private static string MalfunctionText(VtcMaintenanceTruck t)
        {
            if (string.IsNullOrWhiteSpace(t.CurrentIssue))
                return "No active malfunction.";

            return $"{Safe(t.CurrentIssueSeverity)}: {Safe(t.CurrentIssue)}";
        }

        private static string TruckInfoText(VtcMaintenanceTruck t)
        {
            return
                $"Truck ID: {Safe(t.TruckId)}\n" +
                $"Unit #: {Safe(t.UnitNumber)}\n" +
                $"Truck: {Safe(t.TruckName)}\n" +
                $"Plate: {Safe(t.PlateNumber)}\n" +
                $"Driver: {Safe(t.AssignedDriver)}\n" +
                $"Location: {Safe(t.Location)}\n" +
                $"Fuel: {t.FuelPercent:0}%\n" +
                $"Condition: {t.ConditionPercent:0}%";
        }

        private static bool IsServiceDue(VtcMaintenanceTruck truck)
        {
            if (truck.LastServiceUtc == null)
                return true;

            return truck.LastServiceUtc.Value <= DateTime.UtcNow.AddDays(-30);
        }

        private static bool IsInspectionDue(VtcMaintenanceTruck truck)
        {
            if (truck.DotExpirationUtc == null)
                return true;

            return truck.DotExpirationUtc.Value <= DateTime.UtcNow.AddDays(30);
        }

        private static string StatusColor(VtcMaintenanceTruck t)
        {
            if (t.OutOfService || t.ConditionPercent <= 65)
                return "#EF4444";

            if (!string.IsNullOrWhiteSpace(t.CurrentIssue))
                return "#F97316";

            if (IsServiceDue(t) || IsInspectionDue(t))
                return "#F59E0B";

            return "#22C55E";
        }

        private static string NeedsAttentionColor(VtcMaintenanceTruck t)
        {
            return NeedsAttentionText(t)
                .StartsWith("YES", StringComparison.OrdinalIgnoreCase)
                    ? "#EF4444"
                    : "#22C55E";
        }

        private static string InspectionColor(VtcMaintenanceTruck t) =>
            IsInspectionDue(t) ? "#F59E0B" : "#22C55E";

        private static string ServiceColor(VtcMaintenanceTruck t) =>
            IsServiceDue(t) ? "#F59E0B" : "#22C55E";

        private static string MalfunctionColor(VtcMaintenanceTruck t) =>
            string.IsNullOrWhiteSpace(t.CurrentIssue)
                ? "#22C55E"
                : "#EF4444";

        private static Border Card(string title, string body, string accent)
        {
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brush(accent),
                FontWeight = FontWeights.Bold,
                FontSize = 16
            });

            stack.Children.Add(new TextBlock
            {
                Text = body,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });

            return new Border
            {
                Background = Brush("#0B1424"),
                BorderBrush = Brush(accent),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10),
                Child = stack
            };
        }

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a)
                && !string.IsNullOrWhiteSpace(b)
                && string.Equals(
                    a.Trim(),
                    b.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string Safe(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "--"
                : value.Trim();
        }

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }
}