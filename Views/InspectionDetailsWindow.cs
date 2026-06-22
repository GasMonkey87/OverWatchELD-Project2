using OverWatchELD.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Views
{
    public sealed class InspectionDetailsWindow : Window
    {
        private readonly InspectionRecord _record;

        public InspectionDetailsWindow(InspectionRecord record)
        {
            _record = record;

            Title = $"Inspection Details - {_record.InspectionNumber}";
            Width = 720;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07101F");

            Content = Build();
        }

        private UIElement Build()
        {
            var root = new StackPanel { Margin = new Thickness(20) };

            root.Children.Add(Text(_record.InspectionNumber, 26, "#F8FAFC", true));
            root.Children.Add(Text($"{_record.InspectionType} • {_record.CreatedLocalDisplay}", 14, "#9CA3AF", false));

            root.Children.Add(Card("Driver", _record.DriverName));
            root.Children.Add(Card("Truck", $"Truck: {_record.TruckName}\nUnit: {_record.UnitNumber}\nPlate: {_record.PlateNumber}\nLocation: {_record.Location}"));
            root.Children.Add(Card("Result", _record.Passed ? "✅ No defects found" : "⚠️ Defects reported"));
            root.Children.Add(Card("Defects", string.IsNullOrWhiteSpace(_record.Defects) ? "No defects found." : _record.Defects));
            root.Children.Add(Card("Notes", string.IsNullOrWhiteSpace(_record.Notes) ? "No notes." : _record.Notes));

            var close = new Button
            {
                Content = "Close",
                Height = 36,
                Width = 120,
                Margin = new Thickness(0, 14, 0, 0),
                Background = Brush("#334155"),
                Foreground = Brushes.White
            };
            close.Click += (_, _) => Close();

            root.Children.Add(close);

            return new ScrollViewer { Content = root };
        }

        private static Border Card(string title, string body)
        {
            var stack = new StackPanel();

            stack.Children.Add(Text(title, 15, "#38BDF8", true));
            stack.Children.Add(new TextBlock
            {
                Text = body,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

            return new Border
            {
                Background = Brush("#0B1220"),
                BorderBrush = Brush("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 10, 0, 0),
                Child = stack
            };
        }

        private static TextBlock Text(string text, double size, string color, bool bold) => new()
        {
            Text = text,
            FontSize = size,
            Foreground = Brush(color),
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }
}