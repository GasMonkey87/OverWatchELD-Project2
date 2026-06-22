using OverWatchELD.Models.Achievements;
using OverWatchELD.Services.Achievements;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using OverWatchELD.Services;

namespace OverWatchELD.Views.Achievements
{
    public sealed class DriverEndorsementManagerWindow : Window
    {
        private readonly DataGrid _grid = new();
        private readonly TextBox _driverBox = new();
        private readonly TextBox _discordIdBox = new();
        private readonly TextBox _titleBox = new();
        private readonly TextBox _iconBox = new();
        private readonly TextBox _notesBox = new();

        public DriverEndorsementManagerWindow()
        {
            Title = "Driver Endorsements";
            Width = 980;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07111F");

            Content = BuildLayout();

            Loaded += (_, _) => Refresh();
        }

        private UIElement BuildLayout()
        {
            var root = new Grid
            {
                Margin = new Thickness(18)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Driver Endorsements",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 14)
            };

            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var form = new Grid
            {
                Margin = new Thickness(0, 0, 0, 14)
            };

            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddLabeledBox(form, "Driver Name", _driverBox, 0, 0);
            AddLabeledBox(form, "Discord ID optional", _discordIdBox, 0, 1);
            AddLabeledBox(form, "Endorsement Title", _titleBox, 0, 2);
            AddLabeledBox(form, "Icon", _iconBox, 0, 3);

            _iconBox.Text = "⭐";

            var notesLabel = Label("Notes");
            Grid.SetRow(notesLabel, 1);
            Grid.SetColumn(notesLabel, 0);
            form.Children.Add(notesLabel);

            _notesBox.Height = 70;
            _notesBox.AcceptsReturn = true;
            _notesBox.TextWrapping = TextWrapping.Wrap;
            StyleBox(_notesBox);

            Grid.SetRow(_notesBox, 2);
            Grid.SetColumn(_notesBox, 0);
            Grid.SetColumnSpan(_notesBox, 4);
            form.Children.Add(_notesBox);

            Grid.SetRow(form, 1);
            root.Children.Add(form);

            SetupGrid(_grid);
            Grid.SetRow(_grid, 2);
            root.Children.Add(_grid);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var add = Button("Add Endorsement");
            add.Click += (_, _) => AddEndorsement();
            buttons.Children.Add(add);

            var remove = Button("Remove Selected");
            remove.Click += (_, _) => RemoveSelected();
            buttons.Children.Add(remove);

            var refresh = Button("Refresh");
            refresh.Click += (_, _) => Refresh();
            buttons.Children.Add(refresh);

            var close = Button("Close");
            close.Click += (_, _) => Close();
            buttons.Children.Add(close);

            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            return root;
        }

        private void AddEndorsement()
        {
            var driver = (_driverBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(driver))
            {
                MessageBox.Show(
                    "Enter a driver name first.",
                    "Driver Endorsements",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            DriverEndorsementService.Add(
                driver,
                _discordIdBox.Text,
                _titleBox.Text,
                _iconBox.Text,
                _notesBox.Text,
                EldDriverIdentityResolver.DriverName());

            _titleBox.Text = "";
            _notesBox.Text = "";
            _iconBox.Text = "⭐";

            Refresh();
        }

        private void RemoveSelected()
        {
            if (_grid.SelectedItem == null)
                return;

            var id =
                _grid.SelectedItem
                    .GetType()
                    .GetProperty("Id")
                    ?.GetValue(_grid.SelectedItem)
                    ?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(id))
                return;

            DriverEndorsementService.Remove(id);
            Refresh();
        }

        private void Refresh()
        {
            _grid.ItemsSource =
                DriverEndorsementService.LoadAll()
                    .Select(x => new
                    {
                        x.Id,
                        Icon = x.Icon,
                        Driver = x.DriverName,
                        Title = x.Title,
                        Notes = x.Notes,
                        CreatedBy = x.CreatedBy,
                        Created = x.CreatedUtc.ToLocalTime().ToString("g")
                    })
                    .ToList();
        }

        private static void AddLabeledBox(Grid grid, string label, TextBox box, int row, int col)
        {
            var stack = new StackPanel
            {
                Margin = new Thickness(col == 0 ? 0 : 8, 0, 0, 8)
            };

            stack.Children.Add(Label(label));

            box.Height = 34;
            StyleBox(box);
            stack.Children.Add(box);

            Grid.SetRow(stack, row);
            Grid.SetColumn(stack, col);

            grid.Children.Add(stack);
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brush("#9FB3CC"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private static void StyleBox(TextBox box)
        {
            box.Background = Brush("#0D1A2B");
            box.Foreground = Brushes.White;
            box.BorderBrush = Brush("#263E5C");
            box.Padding = new Thickness(10, 5, 10, 5);
        }

        private static Button Button(string text)
        {
            return new Button
            {
                Content = text,
                Height = 38,
                MinWidth = 120,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brush("#163B65"),
                BorderBrush = Brush("#4A91D0"),
                Foreground = Brushes.White,
                Padding = new Thickness(12, 4, 12, 4)
            };
        }

        private static void SetupGrid(DataGrid grid)
        {
            grid.AutoGenerateColumns = true;
            grid.IsReadOnly = true;
            grid.CanUserAddRows = false;
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.Background = Brush("#0D1A2B");
            grid.Foreground = Brushes.White;
            grid.RowBackground = Brush("#0B1626");
            grid.AlternatingRowBackground = Brush("#102038");
            grid.BorderBrush = Brush("#263E5C");
            grid.HorizontalGridLinesBrush = Brush("#263E5C");
            grid.VerticalGridLinesBrush = Brush("#1A2D45");
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;

            grid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
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

            grid.CellStyle = new Style(typeof(DataGridCell))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, Brushes.Transparent),
                    new Setter(Control.ForegroundProperty, Brushes.White),
                    new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6))
                }
            };
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
