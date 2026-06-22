using OverWatchELD.Models.Achievements;
using OverWatchELD.Services.Achievements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using OverWatchELD.Services;

namespace OverWatchELD.Views.Achievements
{
    public sealed class AchievementBoardWindow : Window
    {
        private readonly DataGrid _grid = new();
        private readonly TextBox _searchBox = new();
        private readonly ComboBox _categoryFilter = new();
        private readonly ComboBox _rarityFilter = new();
        private readonly TextBox _driverFilter = new();
        private readonly TextBlock _countText = new();

        private List<AchievementRecord> _allRows = new();

        public AchievementBoardWindow()
        {
            Title = "Achievement Board";
            Width = 1280;
            Height = 780;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07111F");

            Content = BuildLayout();
            Loaded += (_, _) => RefreshBoard();
        }

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Achievement Board",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var filters = BuildFilters();
            Grid.SetRow(filters, 1);
            root.Children.Add(filters);

            SetupGrid();
            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };

            _countText.Foreground = Brush("#9FB3CC");
            _countText.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(_countText, Dock.Left);
            bottom.Children.Add(_countText);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var create = BuildButton("Create");
            create.Click += (_, _) => ShowEditor(null);
            buttons.Children.Add(create);

            var edit = BuildButton("Edit Custom");
            edit.Click += (_, _) => EditSelected();
            buttons.Children.Add(edit);

            var delete = BuildButton("Delete Custom");
            delete.Click += (_, _) => DeleteSelected();
            buttons.Children.Add(delete);

            var award = BuildButton("Award To Driver");
            award.Click += (_, _) => AwardSelected();
            buttons.Children.Add(award);

            var remove = BuildButton("Remove Award");
            remove.Click += (_, _) => RemoveSelectedAward();
            buttons.Children.Add(remove);

            var refresh = BuildButton("Refresh");
            refresh.Click += (_, _) => RefreshBoard();
            buttons.Children.Add(refresh);

            var close = BuildButton("Close");
            close.Click += (_, _) => Close();
            buttons.Children.Add(close);

            DockPanel.SetDock(buttons, Dock.Right);
            bottom.Children.Add(buttons);

            Grid.SetRow(bottom, 3);
            root.Children.Add(bottom);

            return root;
        }

        private UIElement BuildFilters()
        {
            var panel = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 12),
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(Label("Search"));
            SetupBox(_searchBox, 220);
            _searchBox.TextChanged += (_, _) => ApplyFilters();
            panel.Children.Add(_searchBox);

            panel.Children.Add(Label("Category"));
            SetupCombo(_categoryFilter, 160);
            _categoryFilter.SelectionChanged += (_, _) => ApplyFilters();
            panel.Children.Add(_categoryFilter);

            panel.Children.Add(Label("Rarity"));
            SetupCombo(_rarityFilter, 140);
            _rarityFilter.SelectionChanged += (_, _) => ApplyFilters();
            panel.Children.Add(_rarityFilter);

            panel.Children.Add(Label("Driver"));
            SetupBox(_driverFilter, 190);
            _driverFilter.TextChanged += (_, _) => ApplyFilters();
            panel.Children.Add(_driverFilter);

            return panel;
        }

        private void RefreshBoard()
        {
            _allRows = AchievementBoardService.BuildBoard();

            LoadComboValues();
            ApplyFilters();
        }

        private void LoadComboValues()
        {
            var selectedCategory = _categoryFilter.SelectedItem?.ToString() ?? "All";
            var selectedRarity = _rarityFilter.SelectedItem?.ToString() ?? "All";

            _categoryFilter.ItemsSource = new[] { "All" }
                .Concat(_allRows.Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                .ToList();

            _rarityFilter.ItemsSource = new[] { "All", "Common", "Rare", "Epic", "Legendary", "Custom" };

            _categoryFilter.SelectedItem = _categoryFilter.Items.Cast<object>().Select(x => x.ToString()).Contains(selectedCategory)
                ? selectedCategory
                : "All";

            _rarityFilter.SelectedItem = _rarityFilter.Items.Cast<object>().Select(x => x.ToString()).Contains(selectedRarity)
                ? selectedRarity
                : "All";
        }

        private void ApplyFilters()
        {
            var search = (_searchBox.Text ?? "").Trim();
            var category = _categoryFilter.SelectedItem?.ToString() ?? "All";
            var rarity = _rarityFilter.SelectedItem?.ToString() ?? "All";
            var driver = (_driverFilter.Text ?? "").Trim();

            var query = _allRows.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    Contains(x.Title, search) ||
                    Contains(x.Description, search) ||
                    Contains(x.Category, search) ||
                    Contains(x.DriverName, search) ||
                    Contains(x.Rarity, search));
            }

            if (!string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));

            if (!string.Equals(rarity, "All", StringComparison.OrdinalIgnoreCase))
                query = query.Where(x => string.Equals(x.Rarity, rarity, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(driver))
                query = query.Where(x => Contains(x.DriverName, driver));

            var rows = query
                .OrderByDescending(x => x.IsUnlocked)
                .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DriverName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .Select(x => new AchievementGridRow(x))
                .ToList();

            _grid.ItemsSource = rows;
            _countText.Text = $"{rows.Count:N0} shown • {_allRows.Count:N0} total";
        }

        private void ShowEditor(AchievementRecord? existing)
        {
            var isEdit = existing != null;

            if (isEdit && existing!.IsCustom == false)
            {
                MessageBox.Show("System achievements are locked. Create a custom copy or manually award it instead.", "Achievement Board");
                return;
            }

            var window = new Window
            {
                Title = isEdit ? "Edit Custom Achievement" : "Create Custom Achievement",
                Width = 560,
                Height = 600,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#07111F")
            };

            var stack = new StackPanel { Margin = new Thickness(18) };

            stack.Children.Add(FormLabel("Title"));
            var titleBox = Box(existing?.Title ?? "");
            stack.Children.Add(titleBox);

            stack.Children.Add(FormLabel("Description"));
            var descriptionBox = Box(existing?.Description ?? "", 90);
            stack.Children.Add(descriptionBox);

            stack.Children.Add(FormLabel("Icon / Emoji"));
            var iconBox = Box(existing?.Icon ?? "🏆");
            stack.Children.Add(iconBox);

            stack.Children.Add(FormLabel("Awarded To / Driver Name"));
            var awardedToBox = Box(existing?.DriverName ?? "");
            stack.Children.Add(awardedToBox);

            stack.Children.Add(FormLabel("Category"));
            var categoryBox = Box(existing?.Category ?? "Custom");
            stack.Children.Add(categoryBox);

            stack.Children.Add(FormLabel("Rarity"));
            var rarityBox = new ComboBox
            {
                Height = 36,
                ItemsSource = new[] { "Common", "Rare", "Epic", "Legendary", "Custom" },
                SelectedItem = string.IsNullOrWhiteSpace(existing?.Rarity) ? "Common" : existing!.Rarity,
                Background = Brush("#0D1A2B"),
                Foreground = Brushes.White,
                BorderBrush = Brush("#263E5C"),
                Padding = new Thickness(8, 4, 8, 4)
            };
            stack.Children.Add(rarityBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var save = BuildButton(isEdit ? "Save" : "Create");
            save.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(titleBox.Text))
                {
                    MessageBox.Show("Title is required.", "Achievement Board");
                    return;
                }

                if (isEdit)
                {
                    AchievementBoardService.UpdateCustomAchievement(
                        existing!.Id,
                        titleBox.Text,
                        descriptionBox.Text,
                        iconBox.Text,
                        awardedToBox.Text,
                        categoryBox.Text,
                        rarityBox.SelectedItem?.ToString() ?? "Common");
                }
                else
                {
                    AchievementBoardService.AddCustomAchievement(
                        titleBox.Text,
                        descriptionBox.Text,
                        iconBox.Text,
                        awardedToBox.Text,
                        categoryBox.Text,
                        rarityBox.SelectedItem?.ToString() ?? "Common",
                        EldDriverIdentityResolver.DriverName());
                }

                window.Close();
                RefreshBoard();
            };
            buttons.Children.Add(save);

            var cancel = BuildButton("Cancel");
            cancel.Click += (_, _) => window.Close();
            buttons.Children.Add(cancel);

            stack.Children.Add(buttons);

            window.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = stack
            };

            window.ShowDialog();
        }

        private void EditSelected()
        {
            var row = SelectedRow();
            if (row == null) return;

            var record = _allRows.FirstOrDefault(x => string.Equals(x.Id, row.Id, StringComparison.OrdinalIgnoreCase));
            if (record == null) return;

            ShowEditor(record);
        }

        private void DeleteSelected()
        {
            var row = SelectedRow();
            if (row == null) return;

            var record = _allRows.FirstOrDefault(x => string.Equals(x.Id, row.Id, StringComparison.OrdinalIgnoreCase));
            if (record == null) return;

            if (!record.IsCustom)
            {
                MessageBox.Show("System achievements cannot be deleted.", "Achievement Board");
                return;
            }

            var confirm = MessageBox.Show($"Delete custom achievement '{record.Title}'?", "Achievement Board", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            AchievementBoardService.DeleteCustomAchievement(record.Id);
            RefreshBoard();
        }

        private void AwardSelected()
        {
            var row = SelectedRow();
            if (row == null) return;

            var window = new Window
            {
                Title = "Award Achievement To Driver",
                Width = 440,
                Height = 260,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#07111F")
            };

            var stack = new StackPanel { Margin = new Thickness(18) };
            stack.Children.Add(FormLabel("Driver Name"));
            var driverBox = Box(row.DriverName ?? "");
            stack.Children.Add(driverBox);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };

            var award = BuildButton("Award");
            award.Click += (_, _) =>
            {
                try
                {
                    AchievementBoardService.AwardAchievementToDriver(row.Id, driverBox.Text);
                    window.Close();
                    RefreshBoard();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Achievement Board");
                }
            };
            buttons.Children.Add(award);

            var cancel = BuildButton("Cancel");
            cancel.Click += (_, _) => window.Close();
            buttons.Children.Add(cancel);

            stack.Children.Add(buttons);
            window.Content = stack;
            window.ShowDialog();
        }

        private void RemoveSelectedAward()
        {
            var row = SelectedRow();
            if (row == null) return;

            var record = _allRows.FirstOrDefault(x => string.Equals(x.Id, row.Id, StringComparison.OrdinalIgnoreCase));
            if (record == null) return;

            if (!record.IsCustom && !record.Id.StartsWith("manual-", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("System achievements cannot be removed. Only manually-created/manual awards can be removed.", "Achievement Board");
                return;
            }

            var confirm = MessageBox.Show($"Remove award '{record.Title}'?", "Achievement Board", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            AchievementBoardService.RemoveAwardFromDriver(record.Id);
            RefreshBoard();
        }

        private AchievementGridRow? SelectedRow()
        {
            if (_grid.SelectedItem is AchievementGridRow row)
                return row;

            MessageBox.Show("Select an achievement first.", "Achievement Board");
            return null;
        }

        private void SetupGrid()
        {
            _grid.AutoGenerateColumns = true;
            _grid.IsReadOnly = true;
            _grid.CanUserAddRows = false;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.Background = Brush("#0D1A2B");
            _grid.Foreground = Brushes.White;
            _grid.RowBackground = Brush("#0B1626");
            _grid.AlternatingRowBackground = Brush("#102038");
            _grid.BorderBrush = Brush("#263E5C");
            _grid.HorizontalGridLinesBrush = Brush("#263E5C");
            _grid.VerticalGridLinesBrush = Brush("#1A2D45");
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;

            _grid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, Brush("#132238")),
                    new Setter(Control.ForegroundProperty, Brushes.White),
                    new Setter(Control.BorderBrushProperty, Brush("#263E5C")),
                    new Setter(Control.FontWeightProperty, FontWeights.SemiBold),
                    new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8))
                }
            };

            _grid.CellStyle = new Style(typeof(DataGridCell))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, Brushes.Transparent),
                    new Setter(Control.ForegroundProperty, Brushes.White),
                    new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6))
                }
            };
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, Foreground = Brush("#9FB3CC"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        }

        private static TextBlock FormLabel(string text)
        {
            return new TextBlock { Text = text, Foreground = Brush("#9FB3CC"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };
        }

        private static void SetupBox(TextBox box, double width)
        {
            box.Width = width;
            box.Height = 34;
            box.Margin = new Thickness(0, 0, 14, 8);
            box.Background = Brush("#0D1A2B");
            box.Foreground = Brushes.White;
            box.BorderBrush = Brush("#263E5C");
            box.Padding = new Thickness(8, 4, 8, 4);
        }

        private static void SetupCombo(ComboBox combo, double width)
        {
            combo.Width = width;
            combo.Height = 34;
            combo.Margin = new Thickness(0, 0, 14, 8);
            combo.Background = Brush("#0D1A2B");
            combo.Foreground = Brushes.White;
            combo.BorderBrush = Brush("#263E5C");
            combo.Padding = new Thickness(8, 4, 8, 4);
        }

        private static TextBox Box(string text = "", double height = 36)
        {
            return new TextBox
            {
                Text = text,
                Height = height,
                AcceptsReturn = height > 40,
                TextWrapping = height > 40 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalScrollBarVisibility = height > 40 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                Background = Brush("#0D1A2B"),
                Foreground = Brushes.White,
                BorderBrush = Brush("#263E5C"),
                Padding = new Thickness(10, 6, 10, 6)
            };
        }

        private static Button BuildButton(string text)
        {
            return new Button
            {
                Content = text,
                Height = 38,
                MinWidth = 112,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brush("#163B65"),
                BorderBrush = Brush("#4A91D0"),
                Foreground = Brushes.White,
                Padding = new Thickness(12, 4, 12, 4)
            };
        }

        private static bool Contains(string? value, string search)
        {
            return (value ?? "").Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private sealed class AchievementGridRow
        {
            public AchievementGridRow(AchievementRecord record)
            {
                Id = record.Id;
                Status = record.IsUnlocked ? "🏆" : "🔒";
                Icon = record.Icon;
                Category = record.Category;
                Rarity = record.Rarity;
                Title = record.Title;
                Description = record.Description;
                DriverName = record.DriverName;
                Progress = record.ProgressText;
                Reward = record.RewardText;
                Type = record.IsCustom ? "Custom" : "System";
                Earned = record.UnlockedUtc?.ToLocalTime().ToString("g") ?? "--";
            }

            public string Id { get; }
            public string Status { get; }
            public string Icon { get; }
            public string Category { get; }
            public string Rarity { get; }
            public string Title { get; }
            public string Description { get; }
            public string DriverName { get; }
            public string Progress { get; }
            public string Reward { get; }
            public string Type { get; }
            public string Earned { get; }
        }
    }
}
