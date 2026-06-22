using OverWatchELD.Models;
using OverWatchELD.Stores;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using OverWatchELD.Services;

namespace OverWatchELD.Views
{
    public sealed class InspectionRecordWindow : Window
    {
        private readonly string _type;
        private readonly TextBox _truck = Box();
        private readonly TextBox _unit = Box();
        private readonly TextBox _plate = Box();
        private readonly ComboBox _driver = DriverCombo();
        private readonly TextBox _location = Box();
        private readonly CheckBox _passed = new() { Content = "Inspection passed / no defects", IsChecked = true, Foreground = Brushes.White };
        private readonly TextBox _defects = Box();
        private readonly TextBox _notes = Box();

        public InspectionRecordWindow(string type)
        {
            _type = type;
            Title = $"{type} Inspection";
            Width = 620;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07101F");
            DriverDropdownService.Bind(_driver, EldDriverIdentityResolver.DriverName(), includeUnassigned: false);
            Content = Build();
        }

        private UIElement Build()
        {
            var root = new StackPanel { Margin = new Thickness(20) };

            root.Children.Add(Text($"{_type} Inspection", 26, "#F8FAFC", true));
            AddField(root, "Truck", _truck);
            AddField(root, "Unit #", _unit);
            AddField(root, "Plate", _plate);
            AddField(root, "Driver", _driver);
            AddField(root, "Location", _location);

            root.Children.Add(_passed);

            AddField(root, "Defects", _defects, 90);
            AddField(root, "Notes", _notes, 90);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(Button("Save Inspection", "#166534", (_, _) => Save()));
            buttons.Children.Add(Button("Cancel", "#334155", (_, _) => Close()));
            root.Children.Add(buttons);

            return new ScrollViewer { Content = root };
        }

        private void Save()
        {
            var record = new InspectionRecord
            {
                InspectionType = _type,
                TruckName = _truck.Text.Trim(),
                UnitNumber = _unit.Text.Trim(),
                PlateNumber = _plate.Text.Trim(),
                DriverName = DriverDropdownService.SelectedName(_driver, EldDriverIdentityResolver.DriverName()),
                Location = _location.Text.Trim(),
                Passed = _passed.IsChecked == true,
                Defects = _defects.Text.Trim(),
                Notes = _notes.Text.Trim(),
                CreatedUtc = DateTime.UtcNow
            };

            new InspectionRecordStore().Add(record);

            DialogResult = true;
            Close();
        }

        private static void AddField(StackPanel root, string label, Control box, double height = 32)
        {
            root.Children.Add(Text(label, 13, "#9CA3AF", false));
            box.Height = height;
            box.Margin = new Thickness(0, 4, 0, 10);
            root.Children.Add(box);
        }

        private static TextBox Box(string text = "") => new()
        {
            Text = text,
            Background = Brush("#0B1220"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#334155"),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Padding = new Thickness(8)
        };

        private static ComboBox DriverCombo() => new()
        {
            Background = Brush("#0B1220"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#334155"),
            Padding = new Thickness(8),
            IsEditable = false
        };

        private static TextBlock Text(string text, double size, string color, bool bold) => new()
        {
            Text = text,
            FontSize = size,
            Foreground = Brush(color),
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        private static Button Button(string text, string color, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = text,
                Height = 36,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 4, 12, 4),
                Background = Brush(color),
                Foreground = Brushes.White
            };
            b.Click += click;
            return b;
        }

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }
}