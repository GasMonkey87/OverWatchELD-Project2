using OverWatchELD.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Views.Fleet
{
    public sealed class MaintenanceRequestReviewWindow : Window
    {
        private readonly VtcMaintenanceTruck _truck;

        public MaintenanceRequestReviewWindow(VtcMaintenanceTruck truck)
        {
            _truck = truck;

            Title = "Maintenance Requests";
            Width = 720;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07101F");

            Content = Build();
        }

        private UIElement Build()
        {
            var root = new DockPanel
            {
                Margin = new Thickness(20)
            };

            var header = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 14)
            };

            header.Children.Add(new TextBlock
            {
                Text = $"Maintenance Requests - Unit {_truck.UnitNumber}",
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });

            header.Children.Add(new TextBlock
            {
                Text = $"{_truck.TruckName} • Driver: {_truck.AssignedDriver} • Location: {_truck.Location}",
                Foreground = Brush("#9FB4D0"),
                Margin = new Thickness(0, 4, 0, 0)
            });

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var close = new Button
            {
                Content = "Close",
                Height = 38,
                Width = 120,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Brush("#334155"),
                BorderBrush = Brush("#64748B"),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };

            close.Click += (_, _) => Close();

            DockPanel.SetDock(close, Dock.Bottom);
            root.Children.Add(close);

            var list = new StackPanel();

            var records = _truck.ServiceHistory
                .Where(x =>
                    (x.ServiceType ?? "").Contains("Maintenance", StringComparison.OrdinalIgnoreCase) ||
                    (x.ServiceType ?? "").Contains("Malfunction", StringComparison.OrdinalIgnoreCase) ||
                    (x.ServiceType ?? "").Contains("Damage", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CompletedUtc)
                .ToList();

            if (records.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "No maintenance requests or malfunction clear logs found for this truck.",
                    Foreground = Brush("#CBD5E1"),
                    FontSize = 15,
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }
            else
            {
                foreach (var record in records)
                    list.Children.Add(Card(record));
            }

            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list
            });

            return root;
        }

        private static UIElement Card(VtcServiceRecord record)
        {
            var border = new Border
            {
                Background = Brush("#0B1424"),
                BorderBrush = Brush("#1E293B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = record.ServiceType ?? "Maintenance Log",
                Foreground = Brush("#38BDF8"),
                FontWeight = FontWeights.Bold,
                FontSize = 16
            });

            stack.Children.Add(new TextBlock
            {
                Text = $"By: {record.CompletedBy} • Odometer: {record.OdometerMiles:0}",
                Foreground = Brush("#9FB4D0"),
                Margin = new Thickness(0, 4, 0, 6)
            });

            stack.Children.Add(new TextBlock
            {
                Text = record.Notes ?? "",
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });

            border.Child = stack;
            return border;
        }

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }
}