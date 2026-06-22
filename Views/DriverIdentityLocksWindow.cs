using OverWatchELD.Services;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OverWatchELD.Views
{
    public sealed class DriverIdentityLocksWindow : Window
    {
        private readonly DataGrid _locksGrid = new();
        private readonly DataGrid _auditGrid = new();
        private readonly TextBox _renameBox = new();
        private readonly TextBlock _status = new();

        public DriverIdentityLocksWindow()
        {
            Title = "Driver Identity Locks";
            Width = 1100;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = System.Windows.Media.Brushes.Black;
            Foreground = System.Windows.Media.Brushes.White;

            var root = new DockPanel { Margin = new Thickness(14) };
            Content = root;

            var header = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            header.Children.Add(new TextBlock
            {
                Text = "Driver Identity Locks",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White
            });
            header.Children.Add(new TextBlock
            {
                Text = "One Discord ID is allowed to operate under one ELD driver name. Rename or unlock only when fleet management approves it.",
                Opacity = 0.75,
                Foreground = System.Windows.Media.Brushes.White
            });

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(actions, Dock.Top);
            root.Children.Add(actions);

            actions.Children.Add(new TextBlock { Text = "New driver name:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = System.Windows.Media.Brushes.White });
            _renameBox.Width = 220;
            _renameBox.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(_renameBox);

            var renameButton = new Button { Content = "Rename Selected", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
            renameButton.Click += (_, _) => RenameSelected();
            actions.Children.Add(renameButton);

            var unlockButton = new Button { Content = "Unlock Selected", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
            unlockButton.Click += (_, _) => UnlockSelected();
            actions.Children.Add(unlockButton);

            var refreshButton = new Button { Content = "Refresh", Padding = new Thickness(12, 6, 12, 6) };
            refreshButton.Click += (_, _) => Refresh();
            actions.Children.Add(refreshButton);

            _status.Margin = new Thickness(0, 0, 0, 10);
            _status.Foreground = System.Windows.Media.Brushes.LightGreen;
            DockPanel.SetDock(_status, Dock.Top);
            root.Children.Add(_status);

            var tabs = new TabControl();
            root.Children.Add(tabs);

            _locksGrid.AutoGenerateColumns = false;
            _locksGrid.IsReadOnly = true;
            _locksGrid.SelectionMode = DataGridSelectionMode.Single;
            AddColumn(_locksGrid, "Driver Name", "DriverName", 180);
            AddColumn(_locksGrid, "Discord User", "DiscordUsername", 180);
            AddColumn(_locksGrid, "Discord ID", "DiscordUserId", 210);
            AddColumn(_locksGrid, "VTC", "VtcName", 160);
            AddColumn(_locksGrid, "Guild", "GuildId", 160);
            AddColumn(_locksGrid, "Updated UTC", "UpdatedUtc", 180);
            AddColumn(_locksGrid, "Notes", "Notes", 300);
            tabs.Items.Add(new TabItem { Header = "Locks", Content = _locksGrid });

            _auditGrid.AutoGenerateColumns = false;
            _auditGrid.IsReadOnly = true;
            AddColumn(_auditGrid, "Time UTC", "TimestampUtc", 180);
            AddColumn(_auditGrid, "Action", "Action", 130);
            AddColumn(_auditGrid, "Result", "Result", 100);
            AddColumn(_auditGrid, "Discord User", "DiscordUsername", 150);
            AddColumn(_auditGrid, "Discord ID", "DiscordUserId", 200);
            AddColumn(_auditGrid, "Requested", "RequestedDriverName", 150);
            AddColumn(_auditGrid, "Existing", "ExistingDriverName", 150);
            AddColumn(_auditGrid, "Reason", "Reason", 360);
            tabs.Items.Add(new TabItem { Header = "Audit Log", Content = _auditGrid });

            Refresh();
        }

        private static void AddColumn(DataGrid grid, string header, string path, double width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                Width = width
            });
        }

        private DriverIdentityLock? SelectedLock => _locksGrid.SelectedItem as DriverIdentityLock;

        private void Refresh()
        {
            _locksGrid.ItemsSource = DriverIdentityLockService.LoadAll().ToList();
            _auditGrid.ItemsSource = DriverIdentityLockService.LoadAudit().ToList();
            _status.Text = $"Loaded {_locksGrid.Items.Count} identity lock(s).";
        }

        private void RenameSelected()
        {
            var selected = SelectedLock;
            if (selected == null)
            {
                MessageBox.Show("Select an identity lock first.", "Driver Identity Locks", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var newName = (_renameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("Enter the approved new driver name.", "Driver Identity Locks", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"Rename {selected.DriverName} to {newName}?", "Confirm Rename", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            DriverIdentityLockService.Rename(selected.DiscordUserId, newName);
            _renameBox.Text = "";
            _status.Text = "Identity lock renamed.";
            Refresh();
        }

        private void UnlockSelected()
        {
            var selected = SelectedLock;
            if (selected == null)
            {
                MessageBox.Show("Select an identity lock first.", "Driver Identity Locks", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Unlock {selected.DriverName}? This allows this Discord account to bind to a new driver name on next login.", "Confirm Unlock", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            DriverIdentityLockService.Unlock(selected.DiscordUserId);
            _status.Text = "Identity lock removed.";
            Refresh();
        }
    }
}
