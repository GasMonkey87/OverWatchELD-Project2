using OverWatchELD.Models.Dispatch;
using OverWatchELD.Services.Dispatch;
using OverWatchELD.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Views.Dispatch
{
    public sealed class DispatchContractEditWindow : Window
    {
        public DispatchContract? Contract { get; private set; }

        private readonly TextBox _customer = Text("Wallbert Regional");
        private readonly TextBox _type = Text("Dedicated Lane");
        private readonly TextBox _originCity = Text("Chicago");
        private readonly TextBox _originState = Text("IL");
        private readonly TextBox _destCity = Text("St Louis");
        private readonly TextBox _destState = Text("MO");
        private readonly TextBox _cargo = Text("Retail Goods");
        private readonly TextBox _trailer = Text("Dry Van");
        private readonly ComboBox _driver = DriverCombo();
        private readonly TextBox _truck = Text("");
        private readonly TextBox _loads = Text("5");
        private readonly TextBox _miles = Text("300");
        private readonly TextBox _rate = Text("4.25");
        private readonly TextBox _bonus = Text("1000");
        private readonly TextBox _penalty = Text("500");
        private readonly TextBox _days = Text("7");
        private readonly TextBox _notes = Text("");

        public DispatchContractEditWindow()
        {
            Title = "New Dispatch Contract";
            Width = 640;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07111F");

            DriverDropdownService.Bind(_driver, includeUnassigned: true);
            Content = BuildLayout();
        }

        private UIElement BuildLayout()
        {
            var scroll = new ScrollViewer();
            var root = new StackPanel
            {
                Margin = new Thickness(18)
            };

            root.Children.Add(Header("New Dispatch Contract"));

            root.Children.Add(Row("Customer", _customer));
            root.Children.Add(Row("Contract Type", _type));
            root.Children.Add(Row("Origin City", _originCity));
            root.Children.Add(Row("Origin State", _originState));
            root.Children.Add(Row("Destination City", _destCity));
            root.Children.Add(Row("Destination State", _destState));
            root.Children.Add(Row("Cargo", _cargo));
            root.Children.Add(Row("Trailer Type", _trailer));
            root.Children.Add(Row("Assigned Driver", _driver));
            root.Children.Add(Row("Truck # / Name", _truck));
            root.Children.Add(Row("Required Loads", _loads));
            root.Children.Add(Row("Miles Per Load", _miles));
            root.Children.Add(Row("Rate Per Mile", _rate));
            root.Children.Add(Row("Bonus", _bonus));
            root.Children.Add(Row("Penalty", _penalty));
            root.Children.Add(Row("Due In Days", _days));
            root.Children.Add(Row("Notes", _notes));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var cancel = Button("Cancel");
            cancel.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            var save = Button("Create");
            save.Click += (_, _) => Save();

            buttons.Children.Add(cancel);
            buttons.Children.Add(save);

            root.Children.Add(buttons);

            scroll.Content = root;
            return scroll;
        }

        private void Save()
        {
            Contract = new DispatchContract
            {
                ContractNumber = DispatchContractStore.NextContractNumber(),
                CustomerName = _customer.Text.Trim(),
                ContractType = _type.Text.Trim(),
                OriginCity = _originCity.Text.Trim(),
                OriginState = _originState.Text.Trim(),
                DestinationCity = _destCity.Text.Trim(),
                DestinationState = _destState.Text.Trim(),
                Cargo = _cargo.Text.Trim(),
                TrailerType = _trailer.Text.Trim(),
                AssignedDriver = DriverDropdownService.SelectedName(_driver, "Unassigned"),
                AssignedTruckNumber = _truck.Text.Trim(),
                RequiredLoads = ToInt(_loads.Text, 5),
                EstimatedMilesPerLoad = ToDouble(_miles.Text, 300),
                RatePerMile = ToDecimal(_rate.Text, 4.25m),
                BonusAmount = ToDecimal(_bonus.Text, 1000m),
                PenaltyAmount = ToDecimal(_penalty.Text, 500m),
                StartUtc = DateTime.UtcNow,
                DueUtc = DateTime.UtcNow.AddDays(ToInt(_days.Text, 7)),
                Status = "Active",
                Notes = _notes.Text.Trim()
            };

            DialogResult = true;
            Close();
        }

        private static TextBlock Header(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 16)
            };
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

        private static TextBox Text(string value)
        {
            return new TextBox
            {
                Text = value,
                Height = 34,
                Background = Brush("#0D1A2B"),
                Foreground = Brushes.White,
                BorderBrush = Brush("#263E5C"),
                Padding = new Thickness(8, 4, 8, 4)
            };
        }

        private static ComboBox DriverCombo()
        {
            return new ComboBox
            {
                Height = 34,
                Background = Brush("#0D1A2B"),
                Foreground = Brushes.White,
                BorderBrush = Brush("#263E5C"),
                Padding = new Thickness(8, 4, 8, 4),
                IsEditable = false
            };
        }

        private static Button Button(string text)
        {
            return new Button
            {
                Content = text,
                Height = 38,
                MinWidth = 96,
                Margin = new Thickness(8, 0, 0, 0),
                Background = Brush("#163B65"),
                BorderBrush = Brush("#4A91D0"),
                Foreground = Brushes.White,
                Padding = new Thickness(12, 4, 12, 4)
            };
        }

        private static int ToInt(string text, int fallback)
        {
            return int.TryParse(text, out var value) ? value : fallback;
        }

        private static double ToDouble(string text, double fallback)
        {
            return double.TryParse(text, out var value) ? value : fallback;
        }

        private static decimal ToDecimal(string text, decimal fallback)
        {
            return decimal.TryParse(text, out var value) ? value : fallback;
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
