using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using OverWatchELD.Services.Fleet;

namespace OverWatchELD.Services
{
    public sealed class DriverDropdownRow
    {
        public string DriverKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DiscordName { get; set; } = "";
        public string Role { get; set; } = "";

        public override string ToString() => DisplayName;
    }

    public static class DriverDropdownService
    {
        public static List<DriverDropdownRow> LoadDrivers(bool includeUnassigned = true)
        {
            var rows = new List<DriverDropdownRow>();

            if (includeUnassigned)
            {
                rows.Add(new DriverDropdownRow
                {
                    DriverKey = "Unassigned",
                    DisplayName = "Unassigned"
                });
            }

            try
            {
                foreach (var profile in DriverProfileMasterStore.LoadAll())
                {
                    Add(rows,
                        FirstNonBlank(profile.DiscordUserId, profile.DiscordName, profile.DisplayName),
                        FirstNonBlank(profile.DisplayName, profile.DiscordName, profile.DiscordUserId),
                        profile.DiscordUserId,
                        profile.DiscordName,
                        profile.Role);
                }
            }
            catch
            {
            }

            try
            {
                foreach (var truck in new FleetCommandStore().LoadAll())
                {
                    Add(rows,
                        FirstNonBlank(truck.DriverDiscordId, truck.AssignedDriver),
                        truck.AssignedDriver,
                        truck.DriverDiscordId,
                        truck.AssignedDriver,
                        "");
                }
            }
            catch
            {
            }

            try
            {
                if (DispatchService.Drivers != null)
                {
                    foreach (var name in DispatchService.Drivers)
                        Add(rows, name, name, "", name, "");
                }
            }
            catch
            {
            }

            try
            {
                Add(rows,
                    EldCurrentUserService.DiscordUserId,
                    EldCurrentUserService.SafeDisplayName(),
                    EldCurrentUserService.DiscordUserId,
                    EldCurrentUserService.SafeDisplayName(),
                    "");
            }
            catch
            {
            }

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
                .GroupBy(x => NormalizeKey(FirstNonBlank(x.DriverKey, x.DiscordUserId, x.DiscordName, x.DisplayName)), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.DisplayName.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> LoadDriverNames(bool includeUnassigned = true)
        {
            return LoadDrivers(includeUnassigned)
                .Select(x => x.DisplayName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void Bind(ComboBox combo, string? selectedDriver = null, bool includeUnassigned = true)
        {
            if (combo == null)
                return;

            var rows = LoadDrivers(includeUnassigned);

            ApplyDarkComboBox(combo);

            combo.DisplayMemberPath = nameof(DriverDropdownRow.DisplayName);
            combo.SelectedValuePath = nameof(DriverDropdownRow.DriverKey);
            combo.IsEditable = false;
            combo.IsTextSearchEnabled = true;
            combo.ItemsSource = rows;

            Select(combo, selectedDriver);
        }

        public static void Select(ComboBox combo, string? driverNameOrKey)
        {
            if (combo == null)
                return;

            var target = (driverNameOrKey ?? "").Trim();
            var rows = combo.ItemsSource as IEnumerable<DriverDropdownRow>;

            var match = rows?.FirstOrDefault(x =>
                Same(x.DriverKey, target) ||
                Same(x.DisplayName, target) ||
                Same(x.DiscordUserId, target) ||
                Same(x.DiscordName, target));

            if (match != null)
                combo.SelectedItem = match;
            else if (rows != null)
                combo.SelectedItem = rows.FirstOrDefault(x => Same(x.DisplayName, "Unassigned"));
        }

        public static DriverDropdownRow? SelectedRow(ComboBox combo)
        {
            return combo?.SelectedItem as DriverDropdownRow;
        }

        public static string SelectedName(ComboBox combo, string fallback = "")
        {
            return FirstNonBlank(SelectedRow(combo)?.DisplayName, fallback);
        }

        public static string SelectedDiscordId(ComboBox combo, string fallback = "")
        {
            return FirstNonBlank(SelectedRow(combo)?.DiscordUserId, fallback);
        }

        public static string SelectedDiscordName(ComboBox combo, string fallback = "")
        {
            return FirstNonBlank(SelectedRow(combo)?.DiscordName, fallback);
        }

        private static void ApplyDarkComboBox(ComboBox combo)
        {
            try
            {
                combo.Background = Brush("#1B2430");
                combo.Foreground = Brushes.White;
                combo.BorderBrush = Brush("#334155");

                combo.Resources[SystemColors.WindowBrushKey] = Brush("#1B2430");
                combo.Resources[SystemColors.ControlBrushKey] = Brush("#1B2430");
                combo.Resources[SystemColors.ControlTextBrushKey] = Brushes.White;
                combo.Resources[SystemColors.HighlightBrushKey] = Brush("#2F81F7");
                combo.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;

                var itemStyle = new Style(typeof(ComboBoxItem));
                itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#1B2430")));
                itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));

                var hover = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
                hover.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#2F81F7")));
                hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                itemStyle.Triggers.Add(hover);

                combo.ItemContainerStyle = itemStyle;
            }
            catch
            {
            }
        }

        private static Brush Brush(string color) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

        private static void Add(List<DriverDropdownRow> rows, string? key, string? display, string? discordId, string? discordName, string? role)
        {
            var name = FirstNonBlank(display, discordName, discordId, key);
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (name.Equals("Unknown Driver", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Driver", StringComparison.OrdinalIgnoreCase))
                return;

            var cleanKey = FirstNonBlank(key, discordId, discordName, name);
            if (rows.Any(x => Same(x.DriverKey, cleanKey) || Same(x.DisplayName, name) || Same(x.DiscordUserId, discordId)))
                return;

            rows.Add(new DriverDropdownRow
            {
                DriverKey = cleanKey,
                DisplayName = name,
                DiscordUserId = discordId?.Trim() ?? "",
                DiscordName = discordName?.Trim() ?? "",
                Role = role?.Trim() ?? ""
            });
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            return "";
        }

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeKey(string? value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
        }
    }
}
