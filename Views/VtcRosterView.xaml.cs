using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;
using OverWatchELD.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace OverWatchELD.Views
{
    public partial class VtcRosterView : UserControl
    {
        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly DispatcherTimer _liveTimer = new DispatcherTimer();

        public VtcRosterView()
        {
            InitializeComponent();

            if (DataContext == null)
                DataContext = new VtcRosterViewModel();

            Loaded += VtcRosterView_Loaded;

            _liveTimer.Interval = TimeSpan.FromMinutes(5);
            _liveTimer.Tick += async (_, __) =>
            {
                if (DataContext is VtcRosterViewModel vm)
                {
                    await vm.RefreshAsync();
                    ApplyFleetAssignmentsToRoster(vm);
                    vm.ApplyFilter();

                    try
                    {
                        CollectionViewSource.GetDefaultView(vm.Drivers)?.Refresh();
                    }
                    catch { }
                }
            };
        }

        private async void VtcRosterView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is not VtcRosterViewModel vm)
                    return;

                await vm.RefreshAsync();

                ApplyFleetAssignmentsToRoster(vm);

                vm.ApplyFilter();

                try
                {
                    CollectionViewSource.GetDefaultView(vm.Drivers)?.Refresh();
                }
                catch { }
            }
            catch { }
        }

        private async Task OpenAwardManagerAsync(VtcRosterViewModel.RosterDriverRow row)
        {
            try
            {
                if (DataContext is not VtcRosterViewModel vm)
                    return;

                if (!vm.CurrentUserCanManageRoster)
                {
                    MessageBox.Show(
                        "Only Owner/Admin/Manager can manage awards.",
                        "Awards",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var cfg = VtcConfigService.Load(forceReload: true);
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var guildId = (cfg.Discord?.GuildId ?? "").Trim();

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(guildId))
                {
                    MessageBox.Show(
                        "Bot API / Guild is not configured.",
                        "Awards",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var identity = DiscordIdentityStore.Load();
                var currentUserId = (identity?.DiscordUserId ?? "").Trim();
                var currentUserName = (identity?.DiscordUsername ?? "").Trim();

                var win = new AwardManagerWindow(
                    row,
                    baseUrl,
                    guildId,
                    currentUserId,
                    currentUserName)
                {
                    Owner = Window.GetWindow(this)
                };

                var changed = win.ShowDialog();

                if (changed == true)
                {
                    await vm.RefreshAsync();
                    ApplyFleetAssignmentsToRoster(vm);
                    vm.ApplyFilter();

                    try
                    {
                        CollectionViewSource.GetDefaultView(vm.Drivers)?.Refresh();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Awards", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RosterRow_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ClickCount != 2)
                    return;

                if (sender is not FrameworkElement fe)
                    return;

                if (fe.DataContext is not VtcRosterViewModel.RosterDriverRow row)
                    return;

                var win = new DriverProfileView(row, readOnly: true)
                {
                    Owner = Window.GetWindow(this),
                    Title = $"Driver Profile • {row.Driver}"
                };

                win.ShowDialog();

                if (DataContext is VtcRosterViewModel vm)
                {
                    await vm.RefreshAsync();

                    ApplyFleetAssignmentsToRoster(vm);

                    vm.ApplyFilter();

                    try
                    {
                        CollectionViewSource.GetDefaultView(vm.Drivers)?.Refresh();
                    }
                    catch { }
                }

                e.Handled = true;
            }
            catch { }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is not VtcRosterViewModel vm)
                    return;

                await vm.RefreshAsync();

                ApplyFleetAssignmentsToRoster(vm);

                vm.ApplyFilter();

                try
                {
                    CollectionViewSource.GetDefaultView(vm.Drivers)?.Refresh();
                }
                catch { }
            }
            catch { }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (DataContext is not VtcRosterViewModel vm)
                    return;

                vm.SearchText = SearchBox.Text ?? "";

                ApplyFleetAssignmentsToRoster(vm);

                vm.ApplyFilter();

                try
                {
                    CollectionViewSource.GetDefaultView(vm.Drivers)?.Refresh();
                }
                catch { }
            }
            catch { }
        }

        private async void Avatar_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement fe)
                    return;

                VtcRosterViewModel.RosterDriverRow? row = null;

                if (fe.Tag is VtcRosterViewModel.RosterDriverRow tagRow)
                    row = tagRow;
                else if (fe.DataContext is VtcRosterViewModel.RosterDriverRow dcRow)
                    row = dcRow;

                if (row == null)
                    return;

                if (DataContext is not VtcRosterViewModel vm)
                    return;

                var win = new DriverProfileView(row, readOnly: true)
                {
                    Owner = Window.GetWindow(this),
                    Title = $"Driver Profile • {row.Driver}"
                };

                win.ShowDialog();

                await vm.RefreshAsync();

                ApplyFleetAssignmentsToRoster(vm);

                vm.ApplyFilter();

                try
                {
                    CollectionViewSource.GetDefaultView(vm.Drivers)?.Refresh();
                }
                catch { }
            }
            catch { }
        }

        private void MemberMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not VtcRosterViewModel.RosterDriverRow row) return;
            if (DataContext is not VtcRosterViewModel vm) return;

            var menu = new ContextMenu();

            var profileItem = new MenuItem { Header = "Open Profile" };
            profileItem.Click += (_, __) =>
            {
                try
                {
                    var win = new DriverProfileView(row, readOnly: true)
                    {
                        Owner = Window.GetWindow(this),
                        Title = $"Driver Profile • {row.Driver}"
                    };

                    win.ShowDialog();
                }
                catch { }
            };
            menu.Items.Add(profileItem);

            var scoreItem = new MenuItem { Header = "Open Score" };
            scoreItem.Click += (_, __) =>
            {
                try
                {
                    vm.SelectedDriver = row;
                    vm.OpenSelectedDriverProfile();

                    if (vm.OpenSelectedDriverScoreCommand.CanExecute(null))
                        vm.OpenSelectedDriverScoreCommand.Execute(null);
                }
                catch { }
            };
            menu.Items.Add(scoreItem);

            var selfOrAdminCanEdit = vm.CanCurrentUserEditRow(row);

            if (selfOrAdminCanEdit)
            {
                var renameItem = new MenuItem { Header = "Change Name" };
                renameItem.Click += async (_, __) =>
                {
                    var dialog = new RenameDriverWindow(row.Driver)
                    {
                        Owner = Window.GetWindow(this)
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        await vm.ChangeNameAsync(row, dialog.NewDriverName);
                        ApplyFleetAssignmentsToRoster(vm);
                        vm.ApplyFilter();
                    }
                };
                menu.Items.Add(renameItem);

                var truckItem = new MenuItem { Header = "Change Truck" };
                truckItem.Click += async (_, __) =>
                {
                    var dialog = new RenameDriverWindow(row.Truck ?? "")
                    {
                        Owner = Window.GetWindow(this),
                        Title = "Change Truck"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        await vm.ChangeTruckAsync(row, dialog.NewDriverName);
                        ApplyFleetAssignmentsToRoster(vm);
                        vm.ApplyFilter();
                    }
                };
                menu.Items.Add(truckItem);
            }

            if (vm.CurrentUserCanManageRoster)
            {
                var awardsItem = new MenuItem { Header = "Manage Awards" };
                awardsItem.Click += async (_, __) => await OpenAwardManagerAsync(row);
                menu.Items.Add(awardsItem);

                var kickItem = new MenuItem { Header = "Kick" };
                kickItem.Click += async (_, __) =>
                {
                    if (MessageBox.Show($"Kick {row.Driver}?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        await vm.KickAsync(row);
                };
                menu.Items.Add(kickItem);
            }

            btn.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private static void ApplyFleetAssignmentsToRoster(VtcRosterViewModel vm)
        {
            try
            {
                var store = new FleetCommandStore();

                var trucks = store.LoadAll()
                    .Where(t =>
                        !string.IsNullOrWhiteSpace(t.AssignedDriver) ||
                        !string.IsNullOrWhiteSpace(t.DriverDiscordId))
                    .OrderByDescending(t => IsActiveStatus(t.Status))
                    .ThenByDescending(t => t.UpdatedUtc)
                    .ToList();

                foreach (var row in vm.Drivers)
                {
                    if (row == null)
                        continue;

                    var rowDriver = (row.Driver ?? "").Trim();
                    var rowDiscord = (row.DiscordUserId ?? "").Trim();

                    var truck = trucks.FirstOrDefault(t =>
                        Same(t.AssignedDriver, rowDriver) ||
                        ContainsEither(t.AssignedDriver, rowDriver) ||
                        Same(t.DriverDiscordId, rowDiscord));

                    if (truck == null)
                        continue;

                    row.Truck = FirstNonEmpty(
                        truck.TruckName,
                        truck.Model,
                        truck.TruckNumber,
                        truck.PlateNumber);

                    row.Location = FirstNonEmpty(
                        truck.Location,
                        row.Location,
                        "--");
                }
            }
            catch { }
        }

        private void RosterAvatarImage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Ellipse avatar)
                    return;

                avatar.MouseLeftButtonDown -= Avatar_Click;
                avatar.MouseLeftButtonDown += Avatar_Click;

                // IMPORTANT:
                // Do NOT load profile files/images here.
                // This method runs once per roster row on the UI thread.
                // Loading files/images here can freeze the whole app when opening roster.
            }
            catch
            {
            }
        }

        private void RosterAvatarInitials_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock txt)
            {
                txt.MouseLeftButtonDown -= Avatar_Click;
                txt.MouseLeftButtonDown += Avatar_Click;
            }
        }

        private static string GetProfileImagePath(VtcRosterViewModel.RosterDriverRow row)
        {
            try
            {
                var id = FirstNonEmpty(row.DiscordUserId, row.Driver);
                var safe = Sanitize(id);

                var path = IOPath.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Config",
                    "DriverProfiles",
                    $"{safe}.json");

                if (!File.Exists(path))
                    return "";

                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json, _json);

                if (data != null && data.TryGetValue("ProfileImagePath", out var img))
                    return img ?? "";

                return "";
            }
            catch
            {
                return "";
            }
        }

        private static bool IsActiveStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return false;

            var s = status.Trim();

            return s.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Driving", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Online", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("On Duty", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("OnDuty", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsEither(string? a, string? b)
        {
            a = (a ?? "").Trim();
            b = (b ?? "").Trim();

            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;

            return a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                   b.Contains(a, StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }

            return "";
        }

        private static string Sanitize(string value)
        {
            value ??= "default";

            foreach (var c in IOPath.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
        }
    }
}