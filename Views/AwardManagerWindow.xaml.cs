using OverWatchELD.Services;
using OverWatchELD.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace OverWatchELD.Views
{
    public partial class AwardManagerWindow : Window
    {
        private readonly AwardManagerViewModel _vm = new AwardManagerViewModel();
        private readonly VtcRosterViewModel.RosterDriverRow _row;

        private readonly string _baseUrl;
        private readonly string _guildId;
        private readonly string _currentUserId;
        private readonly string _currentUserName;

        public AwardManagerWindow(
            VtcRosterViewModel.RosterDriverRow row,
            string baseUrl,
            string guildId,
            string currentUserId,
            string currentUserName)
        {
            InitializeComponent();

            _row = row;
            _baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            _guildId = (guildId ?? "").Trim();
            _currentUserId = (currentUserId ?? "").Trim();
            _currentUserName = (currentUserName ?? "").Trim();

            DataContext = _vm;

            DriverText.Text = $"Driver: {(_row.Driver ?? "Unknown Driver").Trim()}";
            HeaderText.Text = $"Create awards and assign them to {(_row.Driver ?? "this driver").Trim()}.";
            Loaded += AwardManagerWindow_Loaded;
        }

        private async void AwardManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshAwardsAsync();
        }

        private async Task RefreshAwardsAsync()
        {
            try
            {
                StatusText.Text = "Loading awards...";
                await _vm.LoadAwardsAsync(_baseUrl, _guildId);
                StatusText.Text = "Awards loaded.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Failed to load awards.";
                MessageBox.Show(ex.Message, "Award Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CreateAward_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = (_vm.AwardName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("Enter an award name.", "Award Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusText.Text = "Creating award...";

                var created = await _vm.CreateAwardAsync(_baseUrl, new VtcAwardsApiService.CreateAwardReq
                {
                    GuildId = _guildId,
                    Name = name,
                    Description = (_vm.AwardDescription ?? "").Trim(),
                    IconEmoji = string.IsNullOrWhiteSpace(_vm.AwardEmoji) ? "🏆" : _vm.AwardEmoji.Trim(),
                    IsAchievement = _vm.IsAchievement,
                    CreatedByUserId = _currentUserId,
                    CreatedByUsername = _currentUserName
                });

                if (created == null)
                {
                    StatusText.Text = "Create failed.";
                    MessageBox.Show("Unable to create award.", "Award Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusText.Text = "Award created.";
                MessageBox.Show("Award created.", "Award Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Create failed.";
                MessageBox.Show(ex.Message, "Award Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AssignAward_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm.SelectedAward == null)
                {
                    MessageBox.Show("Select an award first.", "Award Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var driverId = (_row.DiscordUserId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(driverId))
                {
                    MessageBox.Show("This driver does not have a Discord/User ID to assign against.", "Award Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusText.Text = "Assigning award...";

                var ok = await _vm.AssignAwardAsync(_baseUrl, new VtcAwardsApiService.AssignAwardReq
                {
                    GuildId = _guildId,
                    DriverId = driverId,
                    DriverName = (_row.Driver ?? "").Trim(),
                    AwardId = _vm.SelectedAward.Id,
                    AwardedByUserId = _currentUserId,
                    AwardedByUsername = _currentUserName,
                    Note = (_vm.Note ?? "").Trim()
                });

                if (!ok)
                {
                    StatusText.Text = "Assign failed.";
                    MessageBox.Show("Unable to assign award.", "Award Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                DriverProfileMasterStore.AddAward(
                    driverId,
                    _row.Driver,
                    _row.Driver,
                    $"{_vm.SelectedAward.IconEmoji} {_vm.SelectedAward.Name}".Trim());

                StatusText.Text = "Award assigned.";
                MessageBox.Show("Award assigned to driver.", "Award Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Assign failed.";
                MessageBox.Show(ex.Message, "Award Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}