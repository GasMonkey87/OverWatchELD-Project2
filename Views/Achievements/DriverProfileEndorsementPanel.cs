using OverWatchELD.Services.Achievements;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Views.Achievements
{
    public static class DriverProfileEndorsementPanel
    {
        public static UIElement Build(string? driverName, string? driverDiscordId = null)
        {
            var endorsements = DriverEndorsementService.ForDriver(driverName, driverDiscordId);

            var root = new StackPanel
            {
                Margin = new Thickness(0, 10, 0, 0)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Endorsements",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var wrap = new WrapPanel();

            if (endorsements.Count == 0)
            {
                wrap.Children.Add(new TextBlock
                {
                    Text = "No endorsements yet.",
                    Foreground = Brush("#9FB3CC"),
                    FontSize = 12
                });
            }
            else
            {
                foreach (var e in endorsements.OrderByDescending(x => x.CreatedUtc))
                {
                    var badge = new Border
                    {
                        Width = 38,
                        Height = 38,
                        CornerRadius = new CornerRadius(19),
                        Background = Brush("#163B65"),
                        BorderBrush = Brush("#4A91D0"),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 0, 8, 8),
                        ToolTip =
                            $"{e.Title}\n{e.Notes}\nAwarded by {e.CreatedBy} on {e.CreatedUtc.ToLocalTime():g}",
                        Child = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(e.Icon) ? "⭐" : e.Icon,
                            FontSize = 20,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };

                    wrap.Children.Add(badge);
                }
            }

            root.Children.Add(wrap);

            return root;
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
