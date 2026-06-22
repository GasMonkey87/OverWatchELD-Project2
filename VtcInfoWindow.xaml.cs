using System.Windows;
using OverWatchELD.Models;
using OverWatchELD.Services;

namespace OverWatchELD
{
    public partial class VtcInfoWindow : Window
    {
        private VtcInfo _info;

        public VtcInfoWindow()
        {
            InitializeComponent();

            _info = VtcInfoStore.Load();
            DriverDropdownService.Bind(DriverBox, _info.DriverName, includeUnassigned: false);
            LoadToUi(_info);
            ApplyLockUi(_info);
        }

        private void LoadToUi(VtcInfo info)
        {
            CompanyBox.Text = info.CompanyName;
            DriverDropdownService.Select(DriverBox, info.DriverName);
            UnitBox.Text = info.UnitNumber;
            DotBox.Text = info.DotNumber;
            McBox.Text = info.McNumber;
            TerminalBox.Text = info.HomeTerminal;
            NotesBox.Text = info.Notes;

            BotApiBox.Text = info.DiscordLockApiBaseUrl;
            GuildIdBox.Text = info.DiscordGuildId;
            UserIdBox.Text = info.DiscordUserId;

            LockedVtcBox.Text = string.IsNullOrWhiteSpace(info.VtcName) ? info.CompanyName : info.VtcName;
        }

        private void ApplyLockUi(VtcInfo info)
        {
            var configured =
                !string.IsNullOrWhiteSpace(info.DiscordLockApiBaseUrl) &&
                !string.IsNullOrWhiteSpace(info.DiscordGuildId) &&
                !string.IsNullOrWhiteSpace(info.DiscordUserId);

            if (!configured)
            {
                DiscordLockStatusText.Text = "Not configured";
                CompanyBox.IsEnabled = true;
                return;
            }

            if (info.IsLockedToDiscord)
            {
                DiscordLockStatusText.Text = "LOCKED";
                CompanyBox.IsEnabled = false;
            }
            else
            {
                DiscordLockStatusText.Text = "Configured (not locked)";
                CompanyBox.IsEnabled = true;
            }
        }

        private VtcInfo ReadFromUi()
        {
            // Preserve existing info so we don't wipe keys that aren't on the UI.
            var existing = VtcInfoStore.Load();

            var updated = new VtcInfo
            {
                CompanyName = (existing.IsLockedToDiscord ? existing.CompanyName : (CompanyBox.Text?.Trim() ?? "")),
                DriverName = DriverDropdownService.SelectedName(DriverBox, ""),
                UnitNumber = UnitBox.Text?.Trim() ?? "",
                DotNumber = DotBox.Text?.Trim() ?? "",
                McNumber = McBox.Text?.Trim() ?? "",
                HomeTerminal = TerminalBox.Text?.Trim() ?? "",
                Notes = NotesBox.Text ?? "",

                WebhookUrl = existing.WebhookUrl,
                ApiKey = existing.ApiKey,

                IsLockedToDiscord = existing.IsLockedToDiscord,
                DiscordLockApiBaseUrl = BotApiBox.Text?.Trim() ?? "",
                DiscordGuildId = GuildIdBox.Text?.Trim() ?? "",
                DiscordUserId = UserIdBox.Text?.Trim() ?? "",

                VtcId = existing.VtcId,
                VtcName = existing.VtcName,
            };

            return updated;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _info = ReadFromUi();
            VtcInfoStore.Save(_info);

            // Update lock status display immediately
            ApplyLockUi(_info);

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
