using OverWatchELD.Models;
using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;
using OverWatchELD.Stores;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Views.Fleet
{
    public sealed class MaintenanceRequestWindow : Window
    {
        private readonly TextBox _unit = Text("");
        private readonly TextBox _truck = Text("");
        private readonly ComboBox _driver = DriverCombo();
        private readonly TextBox _issue = Text("");
        private readonly TextBox _notes = Text("");

        public MaintenanceRequestWindow()
        {
            Title = "Request Maintenance";
            Width = 560;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07101F");

            Content = Build();

            DriverDropdownService.Bind(_driver, includeUnassigned: false);

            LoadCurrentTruck();
        }

        private UIElement Build()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(22)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Request Maintenance",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Submit a maintenance request to Discord and log it in VTC Maintenance.",
                Foreground = Brush("#9FB4D0"),
                Margin = new Thickness(0, 0, 0, 18)
            });

            root.Children.Add(Row("Unit #", _unit));
            root.Children.Add(Row("Truck", _truck));
            root.Children.Add(Row("Driver", _driver));
            root.Children.Add(Row("Issue", _issue));

            _notes.AcceptsReturn = true;
            _notes.TextWrapping = TextWrapping.Wrap;
            _notes.Height = 90;

            root.Children.Add(Row("Notes", _notes));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var clear = Btn("Clear Malfunctions");
            clear.Background = Brush("#166534");
            clear.BorderBrush = Brush("#22C55E");
            clear.Click += (_, _) => ClearMalfunctions();

            var submit = Btn("Submit Request");
            submit.Click += async (_, _) => await SubmitRequest();

            var cancel = Btn("Cancel");
            cancel.Background = Brush("#334155");
            cancel.Click += (_, _) => Close();

            buttons.Children.Add(clear);
            buttons.Children.Add(cancel);
            buttons.Children.Add(submit);

            root.Children.Add(buttons);

            return root;
        }

        private void LoadCurrentTruck()
        {
            var state = VtcMaintenanceStore.Load();

            var truck =
                state?.Trucks?.FirstOrDefault(t => !t.OutOfService)
                ?? state?.Trucks?.FirstOrDefault();

            if (truck == null)
            {
                DriverDropdownService.Select(_driver, EldCurrentUserService.SafeDisplayName());
                return;
            }

            _unit.Text = truck.UnitNumber ?? "";
            _truck.Text = truck.TruckName ?? "";

            DriverDropdownService.Select(
                _driver,
                FirstNonBlank(truck.AssignedDriver, EldCurrentUserService.SafeDisplayName()));

            _issue.Text = truck.CurrentIssue ?? "";
        }

        private async System.Threading.Tasks.Task SubmitRequest()
        {
            try
            {
                var ticketStore = new MaintenanceRequestTicketStore();

                var ticket = new MaintenanceRequestTicket
                {
                    RequestNumber = $"MR-{DateTime.Now:yyyyMMdd-HHmmss}",
                    UnitNumber = _unit.Text,
                    TruckName = _truck.Text,
                    DriverName = DriverDropdownService.SelectedName(_driver, EldCurrentUserService.SafeDisplayName()),
                    CurrentIssue = _issue.Text,
                    Notes = _notes.Text,
                    Status = "Open",
                    CreatedUtc = DateTime.UtcNow,
                    OtherMaintenanceRequested = true
                };

                ticket = ticketStore.Add(ticket);

                var poster = new MaintenanceRequestDiscordPoster();
                await poster.PostAsync(ticket);

                MessageBox.Show(
                    $"Maintenance ticket {ticket.RequestNumber} submitted.",
                    "Request Sent",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Request Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearMalfunctions()
        {
            try
            {
                VtcMaintenanceRequestService.ClearMalfunctions(
                    _unit.Text,
                    DriverDropdownService.SelectedName(_driver, EldCurrentUserService.SafeDisplayName()),
                    "Driver cleared malfunction from Request Maintenance window.");

                MessageBox.Show(
                    "Malfunctions cleared and logged in VTC Maintenance.",
                    "Malfunctions Cleared",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                _issue.Text = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Clear Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static UIElement Row(string label, Control box)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 12)
            };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brush("#CBD5E1"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            panel.Children.Add(box);

            return panel;
        }

        private static TextBox Text(string value) => new()
        {
            Text = value,
            Height = 38,
            Background = Brush("#0B1220"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#26364F"),
            Padding = new Thickness(9)
        };

        private static ComboBox DriverCombo() => new()
        {
            Height = 38,
            IsEditable = false,
            Background = Brush("#0B1220"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#26364F"),
            Padding = new Thickness(9, 4, 9, 4)
        };

        private static Button Btn(string text) => new()
        {
            Content = text,
            Height = 40,
            MinWidth = 130,
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brush("#1D4ED8"),
            BorderBrush = Brush("#38BDF8"),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold
        };

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }
}
