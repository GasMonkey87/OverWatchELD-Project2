using OverWatchELD.Models;
using OverWatchELD.Services;
using OverWatchELD.Stores;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OverWatchELD.Views
{
    public sealed class MaintenanceRequestFixWindow : Window
    {
        private readonly MaintenanceRequestTicketStore _ticketStore = new();
        private readonly MaintenanceRequestTicket _ticket;
        private readonly TextBox _fixNotesBox;

        public MaintenanceRequestFixWindow(MaintenanceRequestTicket ticket)
        {
            _ticket = ticket;

            Title = $"Fix Maintenance Request {ticket.RequestNumber}";
            Width = 760;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07101F");

            _fixNotesBox = new TextBox
            {
                Text = ticket.FixNotes ?? "",
                AcceptsReturn = true,
                Height = 115,
                TextWrapping = TextWrapping.Wrap,
                Background = Brush("#0B1220"),
                Foreground = Brushes.White,
                BorderBrush = Brush("#26364F"),
                Padding = new Thickness(10)
            };

            Content = Build();
        }

        private UIElement Build()
        {
            var root = new StackPanel { Margin = new Thickness(20) };

            root.Children.Add(Text($"Request {_ticket.RequestNumber}", 28, "#F8FAFC", true));
            root.Children.Add(Text($"{_ticket.Status} • Created {_ticket.CreatedUtc.ToLocalTime():g}", 14, "#9FB4D0", false));

            root.Children.Add(Card("Truck",
                $"Unit: {_ticket.UnitNumber}\nTruck: {_ticket.TruckName}\nPlate: {_ticket.PlateNumber}\nDriver: {_ticket.DriverName}\nLocation: {_ticket.Location}"));

            root.Children.Add(Card("Request",
                $"DOT Inspection: {YesNo(_ticket.DotInspectionRequested)}\nDamage Repair: {YesNo(_ticket.DamageRepairRequested)}\nOther Maintenance: {YesNo(_ticket.OtherMaintenanceRequested)}\nRepair Malfunctions: {YesNo(_ticket.MalfunctionRepairRequested)}\nIssue: {_ticket.CurrentIssueSeverity} {_ticket.CurrentIssue}\nOdometer: {_ticket.OdometerMiles:0}\nCondition: {_ticket.ConditionPercent:0}%\nNotes: {_ticket.Notes}"));

            root.Children.Add(Text("Fix Notes", 16, "#F8FAFC", true));
            root.Children.Add(_fixNotesBox);

            var buttons = new UniformGrid { Columns = 4, Margin = new Thickness(0, 16, 0, 0) };

            buttons.Children.Add(Button("Fix DOT Inspection", "#2563EB", async (_, _) => await FixDot()));
            buttons.Children.Add(Button("Fix Damage", "#6D28D9", async (_, _) => await FixDamage()));
            buttons.Children.Add(Button("Fix Maintenance", "#166534", async (_, _) => await FixMaintenance()));
            buttons.Children.Add(Button("Close", "#334155", (_, _) => Close()));

            root.Children.Add(buttons);

            return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private async System.Threading.Tasks.Task FixDot()
        {
            if (!UpdateTruck(t =>
            {
                t.LastInspectionUtc = DateTime.UtcNow;
                t.DotExpirationUtc = DateTime.UtcNow.AddMonths(12);

                t.ServiceHistory.Add(new VtcServiceRecord
                {
                    ServiceType = $"Request {_ticket.RequestNumber} - DOT Inspection Fixed",
                    Notes = ReadFixNotes("DOT inspection completed from maintenance request."),
                    OdometerMiles = t.OdometerMiles,
                    CompletedBy = CurrentUser()
                });
            })) return;

            await MarkFixed("DOT Inspection");
        }

        private async System.Threading.Tasks.Task FixDamage()
        {
            if (!UpdateTruck(t =>
            {
                foreach (var r in t.DamageReports.Where(x => !x.Resolved))
                    r.Resolved = true;

                t.CurrentIssue = "";
                t.CurrentIssueSeverity = "";
                t.OutOfService = false;
                t.ConditionPercent = Math.Max(t.ConditionPercent, 90);

                t.ServiceHistory.Add(new VtcServiceRecord
                {
                    ServiceType = $"Request {_ticket.RequestNumber} - Damage Fixed",
                    Notes = ReadFixNotes("Damage/malfunction repaired from maintenance request."),
                    OdometerMiles = t.OdometerMiles,
                    CompletedBy = CurrentUser()
                });
            })) return;

            await MarkFixed("Damage");
        }

        private async System.Threading.Tasks.Task FixMaintenance()
        {
            if (!UpdateTruck(t =>
            {
                t.LastServiceUtc = DateTime.UtcNow;
                t.CurrentIssue = "";
                t.CurrentIssueSeverity = "";
                t.OutOfService = false;
                t.ConditionPercent = Math.Max(t.ConditionPercent, 90);

                foreach (var r in t.DamageReports.Where(x => !x.Resolved))
                    r.Resolved = true;

                t.ServiceHistory.Add(new VtcServiceRecord
                {
                    ServiceType = $"Request {_ticket.RequestNumber} - Maintenance Fixed",
                    Notes = ReadFixNotes("Maintenance completed from request."),
                    OdometerMiles = t.OdometerMiles,
                    CompletedBy = CurrentUser()
                });
            })) return;

            await MarkFixed("Maintenance");
        }

        private bool UpdateTruck(Action<VtcMaintenanceTruck> update)
        {
            var state = VtcMaintenanceStore.Load();

            var truck = state.Trucks.FirstOrDefault(t =>
                Same(t.TruckId, _ticket.TruckId) ||
                Same(t.UnitNumber, _ticket.UnitNumber) ||
                Same(t.TruckName, _ticket.TruckName) ||
                Same(t.PlateNumber, _ticket.PlateNumber));

            if (truck == null)
            {
                MessageBox.Show(
                    "Could not find this truck in VTC Maintenance.",
                    "Maintenance Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return false;
            }

            update(truck);
            VtcMaintenanceStore.Save(state);
            return true;
        }

        private async System.Threading.Tasks.Task MarkFixed(string fixedType)
        {
            _ticket.Status = "Fixed";
            _ticket.FixedUtc = DateTime.UtcNow;
            _ticket.FixedBy = CurrentUser();
            _ticket.FixNotes = ReadFixNotes($"{fixedType} fixed.");

            _ticketStore.Update(_ticket);

            try
            {
                var poster = new MaintenanceRequestDiscordPoster();
                await poster.PostFixedAsync(_ticket, fixedType);
            }
            catch
            {
            }

            MessageBox.Show(
                $"Request {_ticket.RequestNumber} marked fixed.",
                "Maintenance Request",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }

        private string ReadFixNotes(string fallback) =>
            string.IsNullOrWhiteSpace(_fixNotesBox.Text) ? fallback : _fixNotesBox.Text.Trim();

        private static string CurrentUser() => EldDriverIdentityResolver.DriverName();

        private static bool Same(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            !string.IsNullOrWhiteSpace(b) &&
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string YesNo(bool value) => value ? "YES" : "No";

        private static TextBlock Text(string text, double size, string color, bool bold) => new()
        {
            Text = text,
            FontSize = size,
            Foreground = Brush(color),
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        private static Border Card(string title, string body)
        {
            var stack = new StackPanel();
            stack.Children.Add(Text(title, 17, "#38BDF8", true));
            stack.Children.Add(new TextBlock
            {
                Text = body,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });

            return new Border
            {
                Background = Brush("#0B1424"),
                BorderBrush = Brush("#26364F"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 10, 0, 10),
                Child = stack
            };
        }

        private static Button Button(string text, string color, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = text,
                Background = Brush(color),
                Foreground = Brushes.White,
                BorderBrush = Brush("#38BDF8"),
                Padding = new Thickness(12),
                Margin = new Thickness(5),
                FontWeight = FontWeights.Bold
            };

            b.Click += click;
            return b;
        }

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }
}