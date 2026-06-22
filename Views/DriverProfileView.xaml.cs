using Microsoft.Win32;
using OverWatchELD.Services.Achievements;
using OverWatchELD.Services.Fleet;
using OverWatchELD.Services.Licensing;
using OverWatchELD.ViewModels;
using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OverWatchELD.Services;

namespace OverWatchELD.Views
{
    public partial class DriverProfileView : Window
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private DriverProfileData _profile = new();
        private object? _rosterSource;
        private bool _readOnly;
        private bool _loaded;

        // One shared saved profile record is used everywhere.
        // Only the normal ELD user profile window is editable.
        private bool IsEditableUserProfile => _rosterSource == null && !_readOnly;

        public DriverProfileView()
        {
            InitializeComponent();
            Loaded += DriverProfileView_Loaded;
        }

        public DriverProfileView(object rosterDriver, bool readOnly = false)
        {
            InitializeComponent();

            _rosterSource = rosterDriver;
            _readOnly = readOnly;

            Loaded += DriverProfileView_Loaded;
        }

        private void DriverProfileView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded)
                return;

            _loaded = true;

            LoadSavedProfileFirst();

            if (_rosterSource != null)
                FillBlanksFromRoster(_rosterSource);

            ApplyMasterProfileToBlanks();
            LoadConnectedTrucks();
            ApplyProfileToUi();

            if (!IsEditableUserProfile)
                SetReadOnlyMode();
        }

        public void LoadProfileFromRoster(object? rosterDriver)
        {
            _rosterSource = rosterDriver;
            _readOnly = true;

            LoadSavedProfileFirst();

            if (_rosterSource != null)
                FillBlanksFromRoster(_rosterSource);

            ApplyMasterProfileToBlanks();
            LoadConnectedTrucks();
            ApplyProfileToUi();

            if (!IsEditableUserProfile)
                SetReadOnlyMode();
        }

        private void LoadSavedProfileFirst()
        {
            var key = GetDriverKey(_rosterSource) ?? "profile:local-driver";

            _profile = new DriverProfileData
            {
                DriverKey = key,
                ProfileId = key
            };

            var primaryPath = GetProfilePath(key);
            if (File.Exists(primaryPath))
            {
                try
                {
                    var json = File.ReadAllText(primaryPath);
                    var saved = JsonSerializer.Deserialize<DriverProfileData>(json, JsonOptions);

                    if (saved != null)
                    {
                        _profile = saved;

                        if (string.IsNullOrWhiteSpace(_profile.DriverKey))
                            _profile.DriverKey = key;

                        if (string.IsNullOrWhiteSpace(_profile.ProfileId))
                            _profile.ProfileId = key;
                    }
                }
                catch
                {
                    _profile = new DriverProfileData
                    {
                        DriverKey = key,
                        ProfileId = key
                    };
                }
            }

            // IMPORTANT:
            // Roster profile windows must use the selected roster driver only.
            // Do NOT pull current logged-in identity into another driver's profile.
            var identityDiscordId = "";
            var identityName = "";

            if (_rosterSource == null)
            {
                identityDiscordId = FirstNonBlank(
                    GetIdentityValue("DiscordUserId"),
                    GetIdentityValue("DriverDiscordUserId"),
                    GetIdentityValue("UserId"),
                    GetIdentityValue("Id"));

                identityName = FirstNonBlank(
                    GetIdentityValue("DiscordName"),
                    GetIdentityValue("Username"),
                    GetIdentityValue("DisplayName"),
                    GetIdentityValue("Name"));
            }

            var rosterDiscordId = _rosterSource == null ? "" : GetValue(_rosterSource, "DiscordUserId", "DriverDiscordUserId", "DriverId", "UserId", "Id") ?? "";
            var rosterDiscordName = _rosterSource == null ? "" : GetValue(_rosterSource, "DiscordName", "DiscordUsername", "Username", "UserName", "Driver") ?? "";
            var rosterDisplayName = _rosterSource == null ? "" : GetValue(_rosterSource, "Driver", "DisplayName", "DriverName", "Name", "Username", "UserName") ?? "";

            FillIfBlank(nameof(DriverProfileData.DriverId), FirstNonBlank(rosterDiscordId, identityDiscordId));
            FillIfBlank(nameof(DriverProfileData.DiscordName), FirstNonBlank(rosterDiscordName, identityName));
            FillIfBlank(nameof(DriverProfileData.DisplayName), FirstNonBlank(rosterDisplayName, identityName));
            FillIfBlank(nameof(DriverProfileData.ProfileId), key);

            var master = DriverProfileMasterStore.Find(
                FirstNonBlank(rosterDiscordId, _profile.DriverId, identityDiscordId),
                FirstNonBlank(rosterDiscordName, _profile.DiscordName, identityName),
                FirstNonBlank(rosterDisplayName, _profile.DisplayName, identityName));

            if (master != null)
            {
                FillIfBlank(nameof(DriverProfileData.DriverId), master.DiscordUserId);
                FillIfBlank(nameof(DriverProfileData.DiscordName), master.DiscordName);
                FillIfBlank(nameof(DriverProfileData.DisplayName), master.DisplayName);
                FillIfBlank(nameof(DriverProfileData.ProfileImagePath), master.PhotoPath);
                FillIfBlank(nameof(DriverProfileData.Role), master.Role);
                FillIfBlank(nameof(DriverProfileData.Status), master.Status);
                FillIfBlank(nameof(DriverProfileData.Location), master.Location);
                FillIfBlank(nameof(DriverProfileData.HomeTerminal), master.HomeTerminal);
                FillIfBlank(nameof(DriverProfileData.Email), master.Email);
                FillIfBlank(nameof(DriverProfileData.Phone), master.Phone);
                FillIfBlank(nameof(DriverProfileData.Bio), master.Bio);
                FillIfBlank(nameof(DriverProfileData.Notes), master.Notes);

                var truck = master.ConnectedTrucks?
                    .OrderByDescending(x => x.IsCurrent)
                    .ThenByDescending(x => x.UpdatedUtc)
                    .FirstOrDefault();

                if (truck != null)
                {
                    FillIfBlank(nameof(DriverProfileData.TruckNumber), truck.TruckNumber);
                    FillIfBlank(nameof(DriverProfileData.TruckName), truck.TruckName);
                }
            }
        }

        private void ApplyMasterProfileToBlanks()
        {
            try
            {
                var master = DriverProfileMasterStore.Find(
                    _profile.DriverId,
                    _profile.DiscordName,
                    _profile.DisplayName);

                if (master == null)
                    return;

                FillIfBlank(nameof(DriverProfileData.DisplayName), master.DisplayName);
                FillIfBlank(nameof(DriverProfileData.DiscordName), master.DiscordName);
                FillIfBlank(nameof(DriverProfileData.DriverId), master.DiscordUserId);
                FillIfBlank(nameof(DriverProfileData.Role), master.Role);
                FillIfBlank(nameof(DriverProfileData.Status), master.Status);
                FillIfBlank(nameof(DriverProfileData.Location), master.Location);
                FillIfBlank(nameof(DriverProfileData.HomeTerminal), master.HomeTerminal);
                FillIfBlank(nameof(DriverProfileData.Email), master.Email);
                FillIfBlank(nameof(DriverProfileData.Phone), master.Phone);
                FillIfBlank(nameof(DriverProfileData.Bio), master.Bio);
                FillIfBlank(nameof(DriverProfileData.Notes), master.Notes);
                FillIfBlank(nameof(DriverProfileData.ProfileImagePath), master.PhotoPath);

                var truck = master.ConnectedTrucks?
                    .OrderByDescending(x => x.IsCurrent)
                    .ThenByDescending(x => x.UpdatedUtc)
                    .FirstOrDefault();

                if (truck != null)
                {
                    FillIfBlank(nameof(DriverProfileData.TruckNumber), truck.TruckNumber);
                    FillIfBlank(nameof(DriverProfileData.TruckName), truck.TruckName);
                }

                var awards = master.Awards?.Count ?? 0;
                var endorsements = master.Endorsements?.Count ?? 0;
                var bols = master.BolLoadNumbers?.Count ?? 0;

                if (awards > 0 || endorsements > 0 || bols > 0)
                {
                    _profile.AchievementsSummary =
                        $"Awards: {awards:N0} • Endorsements: {endorsements:N0} • BOLs: {bols:N0}";
                }
            }
            catch
            {
            }
        }

        private void SaveProfileToMaster()
        {
            try
            {
                DriverProfileMasterStore.SaveProfileDetails(
                    _profile.DriverId,
                    _profile.DiscordName,
                    _profile.DisplayName,
                    photoPath: _profile.ProfileImagePath,
                    role: _profile.Role,
                    status: _profile.Status,
                    location: _profile.Location,
                    homeTerminal: _profile.HomeTerminal,
                    email: _profile.Email,
                    phone: _profile.Phone,
                    bio: _profile.Bio,
                    notes: _profile.Notes);

                DriverProfileMasterStore.LinkTruck(
                    _profile.DriverId,
                    _profile.DiscordName,
                    _profile.DisplayName,
                    _profile.TruckNumber,
                    _profile.TruckName,
                    "",
                    "",
                    "Driver Profile",
                    current: true);
            }
            catch
            {
            }
        }

        private void FillBlanksFromRoster(object row)
        {
            var driver = GetValue(row, "Driver", "DisplayName", "DriverName", "Name", "Username", "UserName", "DiscordUsername");
            var discordName = GetValue(row, "DiscordName", "DiscordUsername", "Username", "UserName", "Driver");
            var discordId = GetValue(row, "DiscordUserId", "DriverDiscordUserId", "DriverId", "UserId", "Id");
            var truck = GetValue(row, "Truck", "TruckName", "CurrentTruck", "VehicleName", "AssignedTruck");
            var location = GetValue(row, "Location", "CurrentLocation", "City", "State");

            FillIfBlank(nameof(DriverProfileData.DisplayName), driver);
            FillIfBlank(nameof(DriverProfileData.DiscordName), discordName);
            FillIfBlank(nameof(DriverProfileData.DriverId), discordId);
            FillIfBlank(nameof(DriverProfileData.ProfileId), discordId ?? driver);
            FillIfBlank(nameof(DriverProfileData.Role), GetValue(row, "Role", "VtcRole", "PermissionRole"));
            FillIfBlank(nameof(DriverProfileData.Status), GetValue(row, "Status", "DutyStatus", "CurrentStatus", "OnlineStatus"));
            FillIfBlank(nameof(DriverProfileData.TruckName), truck);
            FillIfBlank(nameof(DriverProfileData.TruckNumber), GetValue(row, "TruckNumber", "UnitNumber", "TruckId", "Unit", "Truck"));
            FillIfBlank(nameof(DriverProfileData.Location), location);
            FillIfBlank(nameof(DriverProfileData.HomeTerminal), GetValue(row, "HomeTerminal", "Terminal", "Location", "CurrentLocation", "City", "State"));
            FillIfBlank(nameof(DriverProfileData.Email), GetValue(row, "Email", "EmailAddress"));
            FillIfBlank(nameof(DriverProfileData.Phone), GetValue(row, "Phone", "PhoneNumber"));
            FillIfBlank(nameof(DriverProfileData.ProfileImagePath), GetValue(row, "ProfileImagePath", "AvatarPath", "PicturePath", "ImagePath", "PhotoPath"));
            FillIfBlank(nameof(DriverProfileData.AchievementsSummary), GetValue(row, "AchievementsSummary", "AwardCountDisplay", "AwardEmojiSummary"));
        }

        private void FillIfBlank(string propertyName, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var prop = typeof(DriverProfileData).GetProperty(propertyName);
            if (prop == null || !prop.CanWrite)
                return;

            var current = prop.GetValue(_profile)?.ToString();

            if (string.IsNullOrWhiteSpace(current))
                prop.SetValue(_profile, value.Trim());
        }

        private void ApplyProfileToUi()
        {
            DataContext = null;
            DataContext = _profile;

            SetTextBox("DriverNameTextBox", _profile.DisplayName);
            SetTextBox("DiscordNameTextBox", FirstNonBlank(_profile.DiscordName, _profile.DisplayName));
            SetTextBox("DiscordUserIdTextBox", _profile.DriverId);
            SetTextBox("RoleTextBox", _profile.Role);
            SetTextBox("StatusTextBox", _profile.Status);
            SetTextBox("TruckNumberTextBox", _profile.TruckNumber);
            SetTextBox("TruckNameTextBox", _profile.TruckName);
            SetTextBox("FavoriteRouteTextBox", _profile.FavoriteRoute);
            SetTextBox("HomeTerminalTextBox", FirstNonBlank(_profile.HomeTerminal, _profile.Location));
            SetTextBox("ProfileIdTextBox", FirstNonBlank(_profile.ProfileId, _profile.DriverId, _profile.DriverKey));
            SetTextBox("BioTextBox", _profile.Bio);

            SetTextBlock("ProfileStatusText", FirstNonBlank(_profile.AchievementsSummary, "Ready."));

            SetProfileImage(_profile.ProfileImagePath);
            LoadAwardsUi();
            LoadLicenseEndorsementsUi();
            ApplyLicenseEndorsementEditVisibility();
        }

        private void SaveProfileFromUi()
        {
            _profile.DisplayName = GetTextBox("DriverNameTextBox", _profile.DisplayName);
            _profile.DiscordName = GetTextBox("DiscordNameTextBox", _profile.DiscordName);
            _profile.DriverId = GetTextBox("DiscordUserIdTextBox", _profile.DriverId);
            _profile.Role = GetTextBox("RoleTextBox", _profile.Role);
            _profile.Status = GetTextBox("StatusTextBox", _profile.Status);
            _profile.TruckNumber = GetTextBox("TruckNumberTextBox", _profile.TruckNumber);
            _profile.TruckName = GetTextBox("TruckNameTextBox", _profile.TruckName);
            _profile.FavoriteRoute = GetTextBox("FavoriteRouteTextBox", _profile.FavoriteRoute);
            _profile.HomeTerminal = GetTextBox("HomeTerminalTextBox", _profile.HomeTerminal);
            _profile.ProfileId = GetTextBox("ProfileIdTextBox", _profile.ProfileId);
            _profile.Bio = GetTextBox("BioTextBox", _profile.Bio);

            if (string.IsNullOrWhiteSpace(_profile.Location))
                _profile.Location = _profile.HomeTerminal;

            _profile.DriverKey = GetDriverKey(_rosterSource) ?? "profile:local-driver";
            _profile.ProfileId = _profile.DriverKey;

            Directory.CreateDirectory(GetProfilesFolder());

            var json = JsonSerializer.Serialize(_profile, JsonOptions);
            File.WriteAllText(GetProfilePath(_profile.DriverKey), json);

            SaveProfileToMaster();
        }

        private void PickProfilePhoto()
        {
            if (_readOnly)
                return;

            var dlg = new OpenFileDialog
            {
                Title = "Choose Driver Profile Picture",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*"
            };

            if (dlg.ShowDialog(this) != true)
                return;

            _profile.DriverKey = GetDriverKey(_rosterSource) ?? "profile:local-driver";

            Directory.CreateDirectory(GetProfileImagesFolder());

            var ext = Path.GetExtension(dlg.FileName);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";

            var dest = Path.Combine(GetProfileImagesFolder(), $"{SanitizeFileName(_profile.DriverKey)}{ext}");

            File.Copy(dlg.FileName, dest, true);

            _profile.ProfileImagePath = dest;

            DriverProfileMasterStore.SetPhoto(
                _profile.DriverId,
                _profile.DiscordName,
                _profile.DisplayName,
                dest);

            SaveProfileFromUi();
            ApplyProfileToUi();
        }

        private void SetProfileImage(string? path)
        {
            var hasImage = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            BitmapImage? bmp = null;

            if (hasImage)
            {
                try
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bmp.UriSource = new Uri(path!, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                }
                catch
                {
                    bmp = null;
                    hasImage = false;
                }
            }

            SetNamedImage("ProfileImage", bmp, hasImage);
            SetNamedImage("LargeProfileImage", bmp, hasImage);

            SetVisible("ProfileImageFallback", !hasImage);
            SetVisible("LargeProfileFallback", !hasImage);
        }

        private void SetNamedImage(string name, ImageSource? source, bool visible)
        {
            if (FindName(name) is Image img)
            {
                img.Source = source;
                img.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SetVisible(string name, bool visible)
        {
            if (FindName(name) is UIElement element)
                element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadAwardsUi()
        {
            try
            {
                if (AwardsList == null)
                    return;

                var rows = new List<SimpleAwardRow>();

                if (_rosterSource is VtcRosterViewModel.RosterDriverRow row &&
                    row.AwardEmojis != null &&
                    row.AwardEmojis.Count > 0)
                {
                    rows.AddRange(row.AwardEmojis
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(x => new SimpleAwardRow
                        {
                            Award = new SimpleAward
                            {
                                IconEmoji = x,
                                Name = "VTC Award",
                                Description = "Award assigned from the VTC roster."
                            },
                            AwardedUtc = DateTime.Now,
                            Note = "Roster award"
                        }));
                }

                var unlockedAchievements = AchievementBoardService.BuildBoard()
                    .Where(x => x.IsUnlocked)
                    .OrderByDescending(x => x.UnlockedUtc ?? DateTime.MinValue)
                    .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                rows.AddRange(unlockedAchievements.Select(x => new SimpleAwardRow
                {
                    Award = new SimpleAward
                    {
                        IconEmoji = string.IsNullOrWhiteSpace(x.Icon) ? "🏆" : x.Icon,
                        Name = x.Title,
                        Description = FirstNonBlank(x.Description, x.RewardText, "Achievement unlocked")
                    },
                    AwardedUtc = (x.UnlockedUtc ?? DateTime.UtcNow).ToLocalTime(),
                    Note = FirstNonBlank(x.Category, "Achievement") + " • " + FirstNonBlank(x.ProgressText, "Complete")
                }));

                if (rows.Count == 0)
                {
                    rows.Add(new SimpleAwardRow
                    {
                        Award = new SimpleAward
                        {
                            IconEmoji = "🔒",
                            Name = "No Awards Yet",
                            Description = "Awards and achievements will appear here once this driver or VTC unlocks them."
                        },
                        AwardedUtc = DateTime.Now,
                        Note = "Keep driving to unlock achievements."
                    });
                }

                AwardsList.ItemsSource = rows
                    .GroupBy(x => (x.Award.IconEmoji + "|" + x.Award.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderByDescending(x => x.AwardedUtc)
                    .ToList();

                var unlockedCount = unlockedAchievements.Count;
                _profile.AchievementsSummary = unlockedCount > 0
                    ? $"Awards loaded: {rows.Count:N0} • Achievements unlocked: {unlockedCount:N0}"
                    : $"Awards loaded: {rows.Count:N0}";

                SetTextBlock("ProfileStatusText", _profile.AchievementsSummary);
            }
            catch (Exception ex)
            {
                try
                {
                    AwardsList.ItemsSource = new List<SimpleAwardRow>
                    {
                        new SimpleAwardRow
                        {
                            Award = new SimpleAward
                            {
                                IconEmoji = "⚠️",
                                Name = "Awards unavailable",
                                Description = "The awards board could not be loaded."
                            },
                            AwardedUtc = DateTime.Now,
                            Note = ex.Message
                        }
                    };
                }
                catch
                {
                }
            }
        }


        private void LoadLicenseEndorsementsUi()
        {
            try
            {
                if (DotEndorsementsList == null)
                    return;

                var driverKey = FirstNonBlank(
                    _profile.DriverId,
                    _profile.ProfileId,
                    _profile.DiscordName,
                    _profile.DisplayName,
                    GetDriverKey(_rosterSource));

                var roleText = BuildRoleTextForEndorsementScan();
                var rows = DriverLicenseEndorsementService.BuildRows(driverKey, roleText);

                if (rows.Count == 0)
                {
                    rows.Add(new DriverLicenseEndorsementRow
                    {
                        Code = "--",
                        Name = "No DOT endorsements listed.",
                        Icon = "🪪",
                        Source = "No manual or Discord role endorsements found."
                    });
                }

                DotEndorsementsList.ItemsSource = rows;

                var realCount = rows.Count(x => !string.Equals(x.Code, "--", StringComparison.OrdinalIgnoreCase));
                SetTextBlock("DotEndorsementStatusText",
                    realCount > 0
                        ? $"DOT endorsements: {realCount:N0}"
                        : "No DOT endorsements assigned.");
            }
            catch (Exception ex)
            {
                try
                {
                    DotEndorsementsList.ItemsSource = new[]
                    {
                        new DriverLicenseEndorsementRow
                        {
                            Code = "!",
                            Name = "DOT endorsements unavailable.",
                            Icon = "⚠️",
                            Source = ex.Message
                        }
                    };

                    SetTextBlock("DotEndorsementStatusText", "DOT endorsements unavailable.");
                }
                catch
                {
                }
            }
        }

        private string BuildRoleTextForEndorsementScan()
        {
            var values = new List<string>();

            void Add(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value.Trim());
            }

            Add(_profile.Role);

            if (_rosterSource != null)
            {
                Add(GetValue(_rosterSource, "Role", "VtcRole", "PermissionRole"));
                Add(GetValue(_rosterSource, "Roles", "RoleNames", "DiscordRoles", "DiscordRoleNames"));
                Add(GetValue(_rosterSource, "Endorsements", "LicenseEndorsements", "DotEndorsements"));
            }

            return string.Join(" | ", values.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private void ApplyLicenseEndorsementEditVisibility()
        {
            try
            {
                if (ManageDotEndorsementsButton == null)
                    return;

                var canEdit = IsEditableUserProfile && CurrentUserCanManageDriverEndorsements();

                ManageDotEndorsementsButton.Visibility =
                    canEdit
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                ManageDotEndorsementsButton.IsEnabled = canEdit;
            }
            catch
            {
                if (ManageDotEndorsementsButton != null)
                    ManageDotEndorsementsButton.Visibility = Visibility.Collapsed;
            }
        }

        private bool CurrentUserCanManageDriverEndorsements()
        {
            try
            {
                var currentUserId = FirstNonBlank(
                    GetIdentityValue("DiscordUserId"),
                    GetIdentityValue("DriverDiscordUserId"),
                    GetIdentityValue("UserId"),
                    GetIdentityValue("Id"));

                var currentRole = FirstNonBlank(
                    GetIdentityValue("Role"),
                    GetIdentityValue("VtcRole"),
                    GetIdentityValue("PermissionRole"));

                if (RoleCanManage(currentRole))
                    return true;

                // If the current user's role is not stored in the identity file,
                // try to infer it from the roster row only when the profile being
                // opened belongs to the currently logged-in Discord user.
                var targetUserId = FirstNonBlank(_profile.DriverId, GetDriverKey(_rosterSource));

                if (!string.IsNullOrWhiteSpace(currentUserId) &&
                    !string.IsNullOrWhiteSpace(targetUserId) &&
                    Same(currentUserId, targetUserId) &&
                    RoleCanManage(_profile.Role))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string GetIdentityValue(params string[] names)
        {
            try
            {
                var identity = DiscordIdentityService.Load();

                if (identity == null)
                    return "";

                var type = identity.GetType();

                foreach (var name in names)
                {
                    var prop = type.GetProperty(
                        name,
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.IgnoreCase);

                    if (prop != null)
                    {
                        var value = prop.GetValue(identity)?.ToString();

                        if (!string.IsNullOrWhiteSpace(value))
                            return value.Trim();
                    }

                    var field = type.GetField(
                        name,
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.IgnoreCase);

                    if (field != null)
                    {
                        var value = field.GetValue(identity)?.ToString();

                        if (!string.IsNullOrWhiteSpace(value))
                            return value.Trim();
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private static bool RoleCanManage(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return false;

            var r = role.Trim();

            return r.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
                   r.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                   r.Equals("Administrator", StringComparison.OrdinalIgnoreCase) ||
                   r.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
                   r.Contains("owner", StringComparison.OrdinalIgnoreCase) ||
                   r.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
                   r.Contains("manager", StringComparison.OrdinalIgnoreCase) ||
                   r.Contains("management", StringComparison.OrdinalIgnoreCase);
        }

        private void ManageDotEndorsements_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentUserCanManageDriverEndorsements())
            {
                MessageBox.Show(
                    "Only VTC managers, admins, or owners can change DOT endorsements.",
                    "DOT Endorsements",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var driverKey = FirstNonBlank(
                _profile.DriverId,
                _profile.ProfileId,
                _profile.DiscordName,
                _profile.DisplayName,
                GetDriverKey(_rosterSource));

            if (string.IsNullOrWhiteSpace(driverKey))
            {
                MessageBox.Show(
                    "This driver does not have a stable profile key yet.",
                    "DOT Endorsements",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var current = DriverLicenseEndorsementService.GetManualCodes(driverKey);
            var checks = new List<CheckBox>();

            var win = new Window
            {
                Title = "Manage DOT License Endorsements",
                Width = 560,
                Height = 620,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111111"))
            };

            var root = new DockPanel
            {
                Margin = new Thickness(16)
            };

            var title = new TextBlock
            {
                Text = "DOT License Endorsements",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };

            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);

            var help = new TextBlock
            {
                Text = "Manual changes are restricted to managers/admins/owners. Discord endorsement roles are still read automatically and shown separately.",
                Foreground = Brushes.LightGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };

            DockPanel.SetDock(help, Dock.Top);
            root.Children.Add(help);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var save = new Button
            {
                Content = "Save",
                Width = 110,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var cancel = new Button
            {
                Content = "Cancel",
                Width = 110,
                Height = 34
            };

            buttonPanel.Children.Add(save);
            buttonPanel.Children.Add(cancel);

            DockPanel.SetDock(buttonPanel, Dock.Bottom);
            root.Children.Add(buttonPanel);

            var list = new StackPanel();

            foreach (var def in DriverLicenseEndorsementService.StandardDefinitions)
            {
                var cb = new CheckBox
                {
                    Content = $"{def.Icon}  {def.Code} — {def.Name}",
                    Tag = def.Code,
                    IsChecked = current.Contains(def.Code),
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 10),
                    ToolTip = def.Description
                };

                checks.Add(cb);
                list.Children.Add(cb);
            }

            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list
            });

            save.Click += (_, __) =>
            {
                var selected = checks
                    .Where(x => x.IsChecked == true)
                    .Select(x => x.Tag?.ToString() ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                DriverLicenseEndorsementService.SaveManualCodes(driverKey, selected);

                win.Close();

                LoadLicenseEndorsementsUi();
            };

            cancel.Click += (_, __) => win.Close();

            win.Content = root;
            win.ShowDialog();
        }

        private void LoadConnectedTrucks()
        {
            try
            {
                if (ConnectedTrucksGrid == null)
                    return;

                var key = FirstNonBlank(_profile.DriverId, _profile.DiscordName, _profile.DisplayName, GetDriverKey(_rosterSource));
                if (string.IsNullOrWhiteSpace(key))
                    return;

                var store = new FleetCommandStore();
                var rows = store.LoadAll()
                    .Where(t =>
                        Same(t.DriverDiscordId, _profile.DriverId) ||
                        Same(t.AssignedDriver, _profile.DisplayName) ||
                        Same(t.AssignedDriver, _profile.DiscordName) ||
                        ContainsEither(t.AssignedDriver, _profile.DisplayName) ||
                        ContainsEither(t.AssignedDriver, _profile.DiscordName))
                    .Select(t => new ConnectedTruckRow
                    {
                        ActiveMarker = IsActiveStatus(t.Status) ? "Yes" : "",
                        TruckNumber = t.TruckNumber ?? "",
                        TruckName = FirstNonBlank(t.TruckName, t.Model),
                        MakeModel = t.Model ?? "",
                        Plate = t.PlateNumber ?? "",
                        Mileage = t.OdometerMiles > 0 ? t.OdometerMiles.ToString("N0") : "",
                        DamagePercent = t.HealthPercent >= 0 ? (100 - t.HealthPercent).ToString("N0") + "%" : "",
                        FuelPercent = t.FuelPercent > 0 ? t.FuelPercent.ToString("N0") + "%" : "",
                        Location = t.Location ?? "",
                        Status = t.Status ?? ""
                    })
                    .ToList();

                var master = DriverProfileMasterStore.Find(
                    _profile.DriverId,
                    _profile.DiscordName,
                    _profile.DisplayName);

                foreach (var t in master?.ConnectedTrucks ?? new List<DriverTruckLink>())
                {
                    if (rows.Any(x => Same(x.TruckNumber, t.TruckNumber) || Same(x.Plate, t.Plate) || Same(x.TruckName, t.TruckName)))
                        continue;

                    rows.Add(new ConnectedTruckRow
                    {
                        ActiveMarker = t.IsCurrent ? "Yes" : "",
                        TruckNumber = t.TruckNumber ?? "",
                        TruckName = t.TruckName ?? "",
                        MakeModel = "",
                        Plate = t.Plate ?? "",
                        Mileage = "",
                        DamagePercent = "",
                        FuelPercent = "",
                        Location = "",
                        Status = string.IsNullOrWhiteSpace(t.Source) ? "Linked" : t.Source
                    });
                }

                ConnectedTrucksGrid.ItemsSource = rows
                    .OrderByDescending(x => Same(x.ActiveMarker, "Yes"))
                    .ThenBy(x => x.TruckNumber)
                    .ThenBy(x => x.TruckName)
                    .ToList();
            }
            catch
            {
            }
        }

        private void SetReadOnlyMode()
        {
            foreach (var tb in FindVisualChildren<TextBox>(this))
            {
                tb.IsReadOnly = true;
                tb.IsTabStop = false;
            }

            foreach (var btn in FindVisualChildren<Button>(this))
            {
                var text = $"{btn.Name} {btn.Content}".ToLowerInvariant();

                if (text.Contains("save") ||
                    text.Contains("upload") ||
                    text.Contains("remove") ||
                    text.Contains("picture") ||
                    text.Contains("photo") ||
                    text.Contains("manage dot"))
                {
                    btn.Visibility = Visibility.Collapsed;
                    btn.IsEnabled = false;
                }
            }

            if (ManageDotEndorsementsButton != null)
            {
                ManageDotEndorsementsButton.Visibility = Visibility.Collapsed;
                ManageDotEndorsementsButton.IsEnabled = false;
            }
        }

        private void SetTextBox(string name, string? value)
        {
            if (FindName(name) is TextBox tb)
                tb.Text = value ?? "";
        }

        private string GetTextBox(string name, string fallback = "")
        {
            if (FindName(name) is TextBox tb)
                return tb.Text ?? "";

            return fallback ?? "";
        }

        private void SetTextBlock(string name, string? value)
        {
            if (FindName(name) is TextBlock tb)
                tb.Text = value ?? "";
        }

        private static string? GetValue(object source, params string[] names)
        {
            var type = source.GetType();

            foreach (var name in names)
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null)
                {
                    var value = prop.GetValue(source);
                    var text = value?.ToString();

                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }

                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    var value = field.GetValue(source);
                    var text = value?.ToString();

                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
            }

            return null;
        }

        private static string? GetDriverKey(object? source)
        {
            // VTC roster profile: ONLY use the selected roster row.
            // Never fall back to current logged-in identity for roster profiles.
            if (source != null)
            {
                var rosterDiscordId = FirstNonBlank(
                    GetValue(source, "DiscordUserId", "DriverDiscordUserId", "DriverId", "UserId", "Id"));

                if (!string.IsNullOrWhiteSpace(rosterDiscordId))
                    return "discord_" + rosterDiscordId.Trim();

                var rosterName = FirstNonBlank(
                    GetValue(source, "DiscordName", "DiscordUsername", "Username", "UserName"),
                    GetValue(source, "Driver", "DisplayName", "DriverName", "Name"));

                if (!string.IsNullOrWhiteSpace(rosterName))
                    return "driver_" + rosterName.Trim().ToLowerInvariant();

                return null;
            }

            // Local/editable user profile: use logged-in identity.
            var identityDiscordId = FirstNonBlank(
                GetIdentityValue("DiscordUserId"),
                GetIdentityValue("DriverDiscordUserId"),
                GetIdentityValue("UserId"),
                GetIdentityValue("Id"));

            if (!string.IsNullOrWhiteSpace(identityDiscordId))
                return "discord_" + identityDiscordId.Trim();

            var identityName = FirstNonBlank(
                GetIdentityValue("DiscordName"),
                GetIdentityValue("Username"),
                GetIdentityValue("DisplayName"),
                GetIdentityValue("Name"),
                EldDriverIdentityResolver.DriverName(),
                EldCurrentUserService.SafeDisplayName());

            if (!string.IsNullOrWhiteSpace(identityName))
                return "driver_" + identityName.Trim().ToLowerInvariant();

            return "profile_local-driver";
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string GetProfilesFolder()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Config",
                "DriverProfiles");
        }

        private static string GetProfileImagesFolder()
        {
            return Path.Combine(GetProfilesFolder(), "Images");
        }

        private static string GetProfilePath(string key)
        {
            return Path.Combine(GetProfilesFolder(), $"{SanitizeFileName(key)}.json");
        }

        private static string GetLegacyProfilePath(string key)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD",
                "DriverProfiles",
                $"{SanitizeFileName(key)}.json");
        }

        private static string SanitizeFileName(string? value)
        {
            value ??= "local-driver";

            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return string.IsNullOrWhiteSpace(value) ? "local-driver" : value.Trim();
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

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj)
            where T : DependencyObject
        {
            if (depObj == null)
                yield break;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);

                if (child is T t)
                    yield return t;

                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }

        private void UploadPicture_Click(object sender, RoutedEventArgs e) => PickProfilePhoto();
        private void UploadPhotoButton_Click(object sender, RoutedEventArgs e) => PickProfilePhoto();
        private void ChangePhotoButton_Click(object sender, RoutedEventArgs e) => PickProfilePhoto();

        private void RemovePicture_Click(object sender, RoutedEventArgs e)
        {
            _profile.ProfileImagePath = "";
            SetProfileImage("");
            SaveProfileFromUi();
        }

        private void RemovePhotoButton_Click(object sender, RoutedEventArgs e) => RemovePicture_Click(sender, e);

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveProfileFromUi();
            ApplyProfileToUi();

            MessageBox.Show(
                "Driver profile saved.",
                "OverWatch ELD",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e) => Save_Click(sender, e);
        private void Logout_Click(object sender, RoutedEventArgs e) => Close();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        public sealed class DriverProfileData
        {
            public string DriverKey { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string DiscordName { get; set; } = "";
            public string DriverId { get; set; } = "";
            public string Role { get; set; } = "";
            public string Status { get; set; } = "";
            public string Location { get; set; } = "";
            public string TruckNumber { get; set; } = "";
            public string TruckName { get; set; } = "";
            public string FavoriteRoute { get; set; } = "";
            public string HomeTerminal { get; set; } = "";
            public string ProfileId { get; set; } = "";
            public string Email { get; set; } = "";
            public string Phone { get; set; } = "";
            public string Bio { get; set; } = "";
            public string Notes { get; set; } = "";
            public string AchievementsSummary { get; set; } = "";
            public string ProfileImagePath { get; set; } = "";
        }

        private sealed class ConnectedTruckRow
        {
            public string ActiveMarker { get; set; } = "";
            public string TruckNumber { get; set; } = "";
            public string TruckName { get; set; } = "";
            public string MakeModel { get; set; } = "";
            public string Plate { get; set; } = "";
            public string Mileage { get; set; } = "";
            public string DamagePercent { get; set; } = "";
            public string FuelPercent { get; set; } = "";
            public string Location { get; set; } = "";
            public string Status { get; set; } = "";
        }

        private sealed class SimpleAwardRow
        {
            public SimpleAward Award { get; set; } = new();
            public DateTime AwardedUtc { get; set; } = DateTime.Now;
            public string Note { get; set; } = "";
        }

        private sealed class SimpleAward
        {
            public string IconEmoji { get; set; } = "";
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
        }
    }
}
