using OverWatchELD.Models.Fleet;
using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OverWatchELD.Views.Fleet
{
    public sealed class FleetTruckTelemetryImportWindow : Window
    {
        public PendingFleetTruckApproval? PendingTruck { get; private set; }

        private readonly bool _isAdminOrOwner;

        private readonly TextBox _truckNumber = Text("");
        private readonly TextBox _truckName = Text("");
        private readonly TextBox _makeModel = Text("");
        private readonly TextBox _plate = Text("");
        private readonly ComboBox _driver = DriverCombo();
        private readonly TextBox _driverId = Text("");
        private readonly TextBox _location = Text("");
        private readonly TextBox _odometer = Text("0");
        private readonly TextBox _fuel = Text("0");
        private readonly TextBox _health = Text("100");
        private readonly TextBox _damage = Text("0");
        private readonly TextBox _notes = Text("");

        public FleetTruckTelemetryImportWindow(bool isAdminOrOwner = false)
        {
            _isAdminOrOwner = isAdminOrOwner;

            Title = "Add Truck From Telemetry";
            Width = 620;
            Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07111F");

            _truckNumber.IsReadOnly = true;
            _truckNumber.Focusable = false;
            _truckNumber.PreviewTextInput += NumbersOnly;
            DataObject.AddPastingHandler(_truckNumber, PasteNumbersOnly);

            _driverId.IsReadOnly = true;

            PendingTruck = FleetTruckApprovalService.BuildPendingFromTelemetry(null);
            Content = BuildLayout();
            DriverDropdownService.Bind(_driver, includeUnassigned: false);

            LoadPendingIntoFields(PendingTruck);

            _truckNumber.Text = GetNextTruckNumber().ToString();

            AutoImportTelemetry();
            ForceCurrentDiscordUser();
        }

        private UIElement BuildLayout()
        {
            var scroll = new ScrollViewer();
            var root = new StackPanel { Margin = new Thickness(18) };

            root.Children.Add(new TextBlock
            {
                Text = "Review / Fix Truck Information",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text = "This creates a PENDING truck. Management must approve it before it becomes a fleet truck.",
                Foreground = Brush("#9FB3CC"),
                Margin = new Thickness(0, 0, 0, 16)
            });

            var autoImport = Button("Auto Import From ATS Telemetry");
            autoImport.Margin = new Thickness(0, 0, 0, 12);
            autoImport.Background = Brush("#16A34A");
            autoImport.BorderBrush = Brush("#22C55E");
            autoImport.Click += (_, _) => AutoImportTelemetry();
            root.Children.Add(autoImport);

            if (_isAdminOrOwner)
            {
                var adminPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 14)
                };

                var startBox = Text(FleetTruckNumberSettingsService.GetStartingNumber().ToString());
                startBox.Width = 120;
                startBox.PreviewTextInput += NumbersOnly;
                DataObject.AddPastingHandler(startBox, PasteNumbersOnly);

                var saveStart = Button("Set Start #");
                saveStart.Click += (_, _) =>
                {
                    var number = ToInt(startBox.Text);
                    FleetTruckNumberSettingsService.SetStartingNumber(number);
                    _truckNumber.Text = GetNextTruckNumber().ToString();

                    MessageBox.Show(
                        $"Truck numbering will now start at {number}.",
                        "Truck Number Setup",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                };

                adminPanel.Children.Add(new TextBlock
                {
                    Text = "Admin Start #:",
                    Foreground = Brush("#9FB3CC"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });

                adminPanel.Children.Add(startBox);
                adminPanel.Children.Add(saveStart);

                root.Children.Add(adminPanel);
            }

            root.Children.Add(Row("Truck #", _truckNumber));
            root.Children.Add(Row("Truck Name", _truckName));
            root.Children.Add(Row("Make / Model", _makeModel));
            root.Children.Add(Row("Plate #", _plate));
            root.Children.Add(Row("Assigned Driver", _driver));
            root.Children.Add(Row("Driver Discord ID", _driverId));
            root.Children.Add(Row("Current Location", _location));
            root.Children.Add(Row("Odometer Miles", _odometer));
            root.Children.Add(Row("Fuel %", _fuel));
            root.Children.Add(Row("Health %", _health));
            root.Children.Add(Row("Damage %", _damage));
            root.Children.Add(Row("Notes", _notes));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            if (_isAdminOrOwner)
            {
                var delete = Button("Delete Truck");
                delete.Background = Brush("#7F1D1D");
                delete.BorderBrush = Brush("#EF4444");
                delete.Click += (_, _) => DeleteTruck();
                buttons.Children.Add(delete);
            }

            var cancel = Button("Cancel");
            cancel.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            var save = Button("Submit Pending");
            save.Click += (_, _) => SavePending();

            buttons.Children.Add(cancel);
            buttons.Children.Add(save);

            root.Children.Add(buttons);

            scroll.Content = root;
            return scroll;
        }

        private void AutoImportTelemetry()
        {
            try
            {
                var keepTruckNumber = _truckNumber.Text;

                PendingTruck = FleetTruckApprovalService.BuildPendingFromTelemetry(null);

                if (PendingTruck == null)
                    PendingTruck = new PendingFleetTruckApproval();

                LoadPendingIntoFields(PendingTruck);

                _truckNumber.Text = string.IsNullOrWhiteSpace(keepTruckNumber)
                    ? GetNextTruckNumber().ToString()
                    : keepTruckNumber;

                ForceCurrentDiscordUser();

                if (string.IsNullOrWhiteSpace(_truckName.Text))
                    _truckName.Text = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Telemetry Import Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ForceCurrentDiscordUser()
        {
            var user = CurrentDiscordUser();

            if (!string.IsNullOrWhiteSpace(user.Name))
                DriverDropdownService.Select(_driver, user.Name);

            if (!string.IsNullOrWhiteSpace(user.Id))
                _driverId.Text = user.Id;
        }

        private static (string Name, string Id) CurrentDiscordUser()
        {
            try
            {
                var identity = DiscordIdentityStore.Load();

                var name =
                    GetStringProperty(identity, "DiscordUsername") ??
                    GetStringProperty(identity, "DisplayName") ??
                    GetStringProperty(identity, "Username") ??
                    GetStringProperty(identity, "UserName") ??
                    GetStringProperty(identity, "Name") ??
                    "";

                var id =
                    GetStringProperty(identity, "DiscordUserId") ??
                    GetStringProperty(identity, "UserId") ??
                    "";

                return (name.Trim(), id.Trim());
            }
            catch
            {
                return ("", "");
            }
        }

        private static string? GetStringProperty(object? obj, string propertyName)
        {
            if (obj == null)
                return null;

            try
            {
                return obj.GetType()
                    .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(obj)?
                    .ToString();
            }
            catch
            {
                return null;
            }
        }

        private int GetNextTruckNumber()
        {
            var start = FleetTruckNumberSettingsService.GetStartingNumber();

            var used = PendingFleetTruckApprovalStore.LoadAll()
                .Select(x => ToInt(x.TruckNumber))
                .Where(x => x > 0)
                .ToHashSet();

            var fleetStore = new FleetCommandStore();

            var fleetUsed = fleetStore.LoadAll()
                .Select(x => ToInt(x.TruckNumber))
                .Where(x => x > 0);

            foreach (var n in fleetUsed)
                used.Add(n);

            var next = start;

            while (used.Contains(next))
                next++;

            return next;
        }

        private void DeleteTruck()
        {
            if (!_isAdminOrOwner)
            {
                MessageBox.Show(
                    "Only an admin or owner can delete trucks.",
                    "Access Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var truckNumber = _truckNumber.Text.Trim();

            if (string.IsNullOrWhiteSpace(truckNumber))
                return;

            var confirm = MessageBox.Show(
                $"Delete truck #{truckNumber}? This cannot be undone.",
                "Delete Truck",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            PendingFleetTruckApprovalStore.DeleteByTruckNumber(truckNumber);
            DeleteApprovedFleetTruckByNumber(truckNumber);

            MessageBox.Show(
                $"Truck #{truckNumber} was deleted.",
                "Truck Deleted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }

        private static void DeleteApprovedFleetTruckByNumber(string truckNumber)
        {
            var store = new FleetCommandStore();

            var rows = store.LoadAll()
                .Where(x =>
                    !string.Equals(
                        x.TruckNumber,
                        truckNumber,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            store.SaveAll(rows);
        }

        private void LoadPendingIntoFields(PendingFleetTruckApproval pending)
        {
            if (pending == null)
                return;

            _truckNumber.Text = pending.TruckNumber;
            _truckName.Text = pending.TruckName;
            _makeModel.Text = pending.MakeModel;
            _plate.Text = pending.PlateNumber;
            DriverDropdownService.Select(_driver, pending.AssignedDriver);
            _driverId.Text = pending.DriverDiscordId;
            _location.Text = pending.CurrentLocation;
            _odometer.Text = pending.OdometerMiles.ToString("0");
            _fuel.Text = pending.FuelPercent.ToString("0.##");
            _health.Text = pending.HealthPercent.ToString("0.##");
            _damage.Text = pending.DamagePercent.ToString("0.##");
            _notes.Text = pending.Notes;
        }

        private void SavePending()
        {
            ForceCurrentDiscordUser();

            if (string.IsNullOrWhiteSpace(_truckName.Text))
            {
                MessageBox.Show(
                    "Truck Name is required.",
                    "Missing Truck Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var pending = PendingTruck ?? new PendingFleetTruckApproval();

            pending.TruckNumber = _truckNumber.Text.Trim();

            if (string.IsNullOrWhiteSpace(pending.TruckNumber))
                pending.TruckNumber = GetNextTruckNumber().ToString();

            if (string.IsNullOrWhiteSpace(pending.TruckNumber))
            {
                MessageBox.Show("Truck # is required.", "Missing Truck #", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            pending.TruckName = _truckName.Text.Trim();
            pending.MakeModel = _makeModel.Text.Trim();
            pending.PlateNumber = _plate.Text.Trim();
            pending.AssignedDriver = DriverDropdownService.SelectedName(_driver, EldCurrentUserService.SafeDisplayName());
            pending.DriverDiscordId = _driverId.Text.Trim();
            pending.CurrentLocation = _location.Text.Trim();
            pending.OdometerMiles = ToDouble(_odometer.Text);
            pending.FuelPercent = ToDouble(_fuel.Text);
            pending.HealthPercent = ToDouble(_health.Text);
            pending.DamagePercent = ToDouble(_damage.Text);
            pending.Notes = _notes.Text.Trim();
            pending.Status = "Pending";
            pending.Source = "Telemetry";
            pending.UpdatedUtc = DateTime.UtcNow;

            PendingTruck = PendingFleetTruckApprovalStore.Upsert(pending);

            MessageBox.Show(
                "Truck submitted for management approval.",
                "Pending Fleet Truck",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }

        private static void NumbersOnly(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private static void PasteNumbersOnly(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(typeof(string)))
            {
                e.CancelCommand();
                return;
            }

            var text = e.DataObject.GetData(typeof(string)) as string ?? "";

            if (!text.All(char.IsDigit))
                e.CancelCommand();
        }

        private static UIElement Row(string label, Control box)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brush("#9FB3CC"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            panel.Children.Add(box);
            return panel;
        }

        private static TextBox Text(string value) => new()
        {
            Text = value,
            Height = 34,
            Background = Brush("#0D1A2B"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#263E5C"),
            Padding = new Thickness(8, 4, 8, 4)
        };

        private static ComboBox DriverCombo() => new()
        {
            Height = 34,
            Background = Brush("#1B2430"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#263E5C"),
            Padding = new Thickness(8, 4, 8, 4),
            IsEditable = false,
            IsTextSearchEnabled = true
        };

        private static Button Button(string text) => new()
        {
            Content = text,
            Height = 38,
            MinWidth = 110,
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brush("#163B65"),
            BorderBrush = Brush("#4A91D0"),
            Foreground = Brushes.White,
            Padding = new Thickness(12, 4, 12, 4)
        };

        private static double ToDouble(string value) =>
            double.TryParse((value ?? "").Replace("%", "").Replace(",", "").Trim(), out var d) ? d : 0;

        private static int ToInt(string value) =>
            int.TryParse((value ?? "").Replace(",", "").Trim(), out var i) ? i : 0;

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }
}