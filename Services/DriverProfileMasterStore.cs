using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class DriverProfileMaster
    {
        public string DriverId { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DiscordName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string PhotoPath { get; set; } = "";
        public string Role { get; set; } = "";
        public string Status { get; set; } = "";
        public string Location { get; set; } = "";
        public string HomeTerminal { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Bio { get; set; } = "";
        public string Notes { get; set; } = "";

        public List<DriverTruckLink> ConnectedTrucks { get; set; } = new();
        public List<string> Awards { get; set; } = new();
        public List<string> Endorsements { get; set; } = new();
        public List<string> BolLoadNumbers { get; set; } = new();

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class DriverTruckLink
    {
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string Plate { get; set; } = "";
        public string Vin { get; set; } = "";
        public string Source { get; set; } = "";
        public bool IsCurrent { get; set; }
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public static class DriverProfileMasterStore
    {
        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string StorePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OverWatchELD",
                "driver_profiles_master.json");

        public static List<DriverProfileMaster> LoadAll()
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(StorePath))
                        return new List<DriverProfileMaster>();

                    var json = File.ReadAllText(StorePath);
                    var rows = JsonSerializer.Deserialize<List<DriverProfileMaster>>(json, JsonOptions)
                               ?? new List<DriverProfileMaster>();

                    return Deduplicate(rows);
                }
                catch
                {
                    return new List<DriverProfileMaster>();
                }
            }
        }

        public static void SaveAll(IEnumerable<DriverProfileMaster>? profiles)
        {
            lock (Gate)
            {
                try
                {
                    SaveAllUnsafe(Deduplicate((profiles ?? Enumerable.Empty<DriverProfileMaster>()).ToList()));
                }
                catch
                {
                }
            }
        }

        public static DriverProfileMaster? Find(string? discordUserId, string? discordName, string? displayName)
        {
            lock (Gate)
            {
                return Clone(FindBestUnsafe(LoadAllUnsafe(), discordUserId, discordName, displayName));
            }
        }

        public static DriverProfileMaster GetOrCreate(string? discordUserId, string? discordName, string? displayName)
        {
            lock (Gate)
            {
                var all = LoadAllUnsafe();
                var existing = FindBestUnsafe(all, discordUserId, discordName, displayName);

                if (existing != null)
                    return Clone(existing)!;

                return new DriverProfileMaster
                {
                    DiscordUserId = Clean(discordUserId),
                    DiscordName = Clean(discordName),
                    DisplayName = FirstNonBlank(displayName, discordName, discordUserId, "ELD Driver"),
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                };
            }
        }

        public static void Upsert(DriverProfileMaster? incoming)
        {
            if (incoming == null || IsBlankProfile(incoming))
                return;

            lock (Gate)
            {
                var all = LoadAllUnsafe();
                var existing = FindBestUnsafe(all, incoming.DiscordUserId, incoming.DiscordName, incoming.DisplayName);

                if (existing == null)
                {
                    var copy = Clone(incoming)!;
                    if (copy.CreatedUtc == default)
                        copy.CreatedUtc = DateTime.UtcNow;
                    copy.UpdatedUtc = DateTime.UtcNow;
                    all.Add(copy);
                }
                else
                {
                    MergeInto(existing, incoming);
                    existing.UpdatedUtc = DateTime.UtcNow;
                }

                SaveAllUnsafe(Deduplicate(all));
            }
        }

        public static void SaveProfileDetails(
            string? discordUserId,
            string? discordName,
            string? displayName,
            string? photoPath = null,
            string? role = null,
            string? status = null,
            string? location = null,
            string? homeTerminal = null,
            string? email = null,
            string? phone = null,
            string? bio = null,
            string? notes = null)
        {
            lock (Gate)
            {
                var all = LoadAllUnsafe();
                var profile = FindBestUnsafe(all, discordUserId, discordName, displayName);

                if (profile == null)
                {
                    profile = new DriverProfileMaster
                    {
                        DiscordUserId = Clean(discordUserId),
                        DiscordName = Clean(discordName),
                        DisplayName = FirstNonBlank(displayName, discordName, discordUserId, "ELD Driver"),
                        CreatedUtc = DateTime.UtcNow
                    };
                    all.Add(profile);
                }

                if (!string.IsNullOrWhiteSpace(discordUserId)) profile.DiscordUserId = discordUserId.Trim();
                if (!string.IsNullOrWhiteSpace(discordName)) profile.DiscordName = discordName.Trim();
                if (!string.IsNullOrWhiteSpace(displayName)) profile.DisplayName = displayName.Trim();
                if (!string.IsNullOrWhiteSpace(photoPath)) profile.PhotoPath = photoPath.Trim();
                if (!string.IsNullOrWhiteSpace(role)) profile.Role = role.Trim();
                if (!string.IsNullOrWhiteSpace(status)) profile.Status = status.Trim();
                if (!string.IsNullOrWhiteSpace(location)) profile.Location = location.Trim();
                if (!string.IsNullOrWhiteSpace(homeTerminal)) profile.HomeTerminal = homeTerminal.Trim();
                if (!string.IsNullOrWhiteSpace(email)) profile.Email = email.Trim();
                if (!string.IsNullOrWhiteSpace(phone)) profile.Phone = phone.Trim();
                if (!string.IsNullOrWhiteSpace(bio)) profile.Bio = bio.Trim();
                if (!string.IsNullOrWhiteSpace(notes)) profile.Notes = notes.Trim();

                profile.UpdatedUtc = DateTime.UtcNow;
                SaveAllUnsafe(Deduplicate(all));
            }
        }

        public static void LinkTruck(
            string? discordUserId,
            string? discordName,
            string? displayName,
            string? truckNumber,
            string? truckName,
            string? plate,
            string? vin,
            string? source,
            bool current)
        {
            if (string.IsNullOrWhiteSpace(truckNumber) &&
                string.IsNullOrWhiteSpace(truckName) &&
                string.IsNullOrWhiteSpace(plate) &&
                string.IsNullOrWhiteSpace(vin))
                return;

            lock (Gate)
            {
                var all = LoadAllUnsafe();
                var profile = FindBestUnsafe(all, discordUserId, discordName, displayName);

                if (profile == null)
                {
                    profile = new DriverProfileMaster
                    {
                        DiscordUserId = Clean(discordUserId),
                        DiscordName = Clean(discordName),
                        DisplayName = FirstNonBlank(displayName, discordName, discordUserId, "ELD Driver"),
                        CreatedUtc = DateTime.UtcNow
                    };
                    all.Add(profile);
                }

                if (current)
                {
                    foreach (var t in profile.ConnectedTrucks)
                        t.IsCurrent = false;
                }

                var existing = profile.ConnectedTrucks.FirstOrDefault(t =>
                    SameNonBlank(t.TruckNumber, truckNumber) ||
                    SameNonBlank(t.Vin, vin) ||
                    SameNonBlank(t.Plate, plate) ||
                    SameNonBlank(t.TruckName, truckName));

                if (existing == null)
                {
                    existing = new DriverTruckLink();
                    profile.ConnectedTrucks.Add(existing);
                }

                if (string.IsNullOrWhiteSpace(existing.TruckNumber) && !string.IsNullOrWhiteSpace(truckNumber))
                    existing.TruckNumber = truckNumber.Trim();

                if (string.IsNullOrWhiteSpace(existing.TruckName) && !string.IsNullOrWhiteSpace(truckName))
                    existing.TruckName = truckName.Trim();

                if (string.IsNullOrWhiteSpace(existing.Plate) && !string.IsNullOrWhiteSpace(plate))
                    existing.Plate = plate.Trim();

                if (string.IsNullOrWhiteSpace(existing.Vin) && !string.IsNullOrWhiteSpace(vin))
                    existing.Vin = vin.Trim();

                if (string.IsNullOrWhiteSpace(existing.Source) && !string.IsNullOrWhiteSpace(source))
                    existing.Source = source.Trim();

                existing.IsCurrent = current || existing.IsCurrent;
                existing.UpdatedUtc = DateTime.UtcNow;
                profile.UpdatedUtc = DateTime.UtcNow;

                SaveAllUnsafe(Deduplicate(all));
            }
        }

        public static void AddBol(string? discordUserId, string? discordName, string? displayName, string? loadNumber)
        {
            AddUniqueValue(discordUserId, discordName, displayName, loadNumber, x => x.BolLoadNumbers, 250);
        }

        public static void AddAward(string? discordUserId, string? discordName, string? displayName, string? award)
        {
            AddUniqueValue(discordUserId, discordName, displayName, award, x => x.Awards, 500);
        }

        public static void AddEndorsement(string? discordUserId, string? discordName, string? displayName, string? endorsement)
        {
            AddUniqueValue(discordUserId, discordName, displayName, endorsement, x => x.Endorsements, 500);
        }

        public static void SetPhoto(string? discordUserId, string? discordName, string? displayName, string? photoPath)
        {
            if (string.IsNullOrWhiteSpace(photoPath))
                return;

            SaveProfileDetails(
                discordUserId,
                discordName,
                displayName,
                photoPath: photoPath);
        }

        public static DriverProfileMaster CurrentIdentityProfile()
        {
            var snap = DriverProfileIdentitySnapshot.Current();
            return GetOrCreate(snap.DiscordUserId, snap.DiscordName, snap.DisplayName);
        }

        private static void AddUniqueValue(
            string? discordUserId,
            string? discordName,
            string? displayName,
            string? value,
            Func<DriverProfileMaster, List<string>> selector,
            int limit)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            lock (Gate)
            {
                var all = LoadAllUnsafe();
                var profile = FindBestUnsafe(all, discordUserId, discordName, displayName);

                if (profile == null)
                {
                    profile = new DriverProfileMaster
                    {
                        DiscordUserId = Clean(discordUserId),
                        DiscordName = Clean(discordName),
                        DisplayName = FirstNonBlank(displayName, discordName, discordUserId, "ELD Driver"),
                        CreatedUtc = DateTime.UtcNow
                    };
                    all.Add(profile);
                }

                var list = selector(profile);
                var clean = value.Trim();

                if (!list.Any(x => Same(x, clean)))
                    list.Insert(0, clean);

                if (limit > 0 && list.Count > limit)
                    list.RemoveRange(limit, list.Count - limit);

                profile.UpdatedUtc = DateTime.UtcNow;
                SaveAllUnsafe(Deduplicate(all));
            }
        }

        private static List<DriverProfileMaster> LoadAllUnsafe()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new List<DriverProfileMaster>();

                var json = File.ReadAllText(StorePath);
                var rows = JsonSerializer.Deserialize<List<DriverProfileMaster>>(json, JsonOptions)
                           ?? new List<DriverProfileMaster>();

                return Deduplicate(rows);
            }
            catch
            {
                return new List<DriverProfileMaster>();
            }
        }

        private static void SaveAllUnsafe(List<DriverProfileMaster> rows)
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(StorePath, JsonSerializer.Serialize(rows, JsonOptions));
        }

        private static List<DriverProfileMaster> Deduplicate(List<DriverProfileMaster> rows)
        {
            var result = new List<DriverProfileMaster>();

            foreach (var row in rows.Where(x => !IsBlankProfile(x)))
            {
                var existing = FindBestUnsafe(result, row.DiscordUserId, row.DiscordName, row.DisplayName);

                if (existing == null)
                    result.Add(Clone(row)!);
                else
                    MergeInto(existing, row);
            }

            return result
                .OrderBy(x => FirstNonBlank(x.DisplayName, x.DiscordName, x.DiscordUserId))
                .ToList();
        }

        private static DriverProfileMaster? FindBestUnsafe(
    List<DriverProfileMaster> all,
    string? discordUserId,
    string? discordName,
    string? displayName)
        {
            var id = Clean(discordUserId);
            var dn = Clean(discordName);
            var name = Clean(displayName);

            // Discord ID is the only hard identity match.
            if (!string.IsNullOrWhiteSpace(id))
            {
                var byId = all.FirstOrDefault(x => Same(x.DiscordUserId, id));
                if (byId != null)
                    return byId;

                // If searching by a Discord ID and no ID match exists,
                // do NOT match another driver's profile by name.
                return null;
            }

            // Name fallback is allowed only when BOTH sides have no Discord ID.
            if (!string.IsNullOrWhiteSpace(dn))
            {
                var byDiscordName = all.FirstOrDefault(x =>
                    string.IsNullOrWhiteSpace(x.DiscordUserId) &&
                    Same(x.DiscordName, dn));

                if (byDiscordName != null)
                    return byDiscordName;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var byName = all.FirstOrDefault(x =>
                    string.IsNullOrWhiteSpace(x.DiscordUserId) &&
                    Same(x.DisplayName, name));

                if (byName != null)
                    return byName;
            }

            return null;
        }


        private static void MergeInto(DriverProfileMaster target, DriverProfileMaster source)
        {
            if (string.IsNullOrWhiteSpace(target.DiscordUserId) && !string.IsNullOrWhiteSpace(source.DiscordUserId))
                target.DiscordUserId = source.DiscordUserId.Trim();

            if (string.IsNullOrWhiteSpace(target.DiscordName) && !string.IsNullOrWhiteSpace(source.DiscordName))
                target.DiscordName = source.DiscordName.Trim();

            if (string.IsNullOrWhiteSpace(target.DisplayName) && !string.IsNullOrWhiteSpace(source.DisplayName))
                target.DisplayName = source.DisplayName.Trim();

            if (string.IsNullOrWhiteSpace(target.PhotoPath) && !string.IsNullOrWhiteSpace(source.PhotoPath))
                target.PhotoPath = source.PhotoPath.Trim();

            if (string.IsNullOrWhiteSpace(target.Role) && !string.IsNullOrWhiteSpace(source.Role))
                target.Role = source.Role.Trim();

            if (string.IsNullOrWhiteSpace(target.Status) && !string.IsNullOrWhiteSpace(source.Status))
                target.Status = source.Status.Trim();

            if (string.IsNullOrWhiteSpace(target.Location) && !string.IsNullOrWhiteSpace(source.Location))
                target.Location = source.Location.Trim();

            if (string.IsNullOrWhiteSpace(target.HomeTerminal) && !string.IsNullOrWhiteSpace(source.HomeTerminal))
                target.HomeTerminal = source.HomeTerminal.Trim();

            if (string.IsNullOrWhiteSpace(target.Email) && !string.IsNullOrWhiteSpace(source.Email))
                target.Email = source.Email.Trim();

            if (string.IsNullOrWhiteSpace(target.Phone) && !string.IsNullOrWhiteSpace(source.Phone))
                target.Phone = source.Phone.Trim();

            if (string.IsNullOrWhiteSpace(target.Bio) && !string.IsNullOrWhiteSpace(source.Bio))
                target.Bio = source.Bio.Trim();

            if (string.IsNullOrWhiteSpace(target.Notes) && !string.IsNullOrWhiteSpace(source.Notes))
                target.Notes = source.Notes.Trim();

            foreach (var truck in source.ConnectedTrucks ?? new List<DriverTruckLink>())
                MergeTruck(target, truck);

            MergeStrings(target.Awards, source.Awards);
            MergeStrings(target.Endorsements, source.Endorsements);
            MergeStrings(target.BolLoadNumbers, source.BolLoadNumbers);

            if (target.CreatedUtc == default || (source.CreatedUtc != default && source.CreatedUtc < target.CreatedUtc))
                target.CreatedUtc = source.CreatedUtc;

            target.UpdatedUtc = DateTime.UtcNow;
        }

        private static void MergeTruck(DriverProfileMaster profile, DriverTruckLink truck)
        {
            var existing = profile.ConnectedTrucks.FirstOrDefault(t =>
                SameNonBlank(t.TruckNumber, truck.TruckNumber) ||
                SameNonBlank(t.Vin, truck.Vin) ||
                SameNonBlank(t.Plate, truck.Plate) ||
                SameNonBlank(t.TruckName, truck.TruckName));

            if (existing == null)
            {
                profile.ConnectedTrucks.Add(new DriverTruckLink
                {
                    TruckNumber = Clean(truck.TruckNumber),
                    TruckName = Clean(truck.TruckName),
                    Plate = Clean(truck.Plate),
                    Vin = Clean(truck.Vin),
                    Source = Clean(truck.Source),
                    IsCurrent = truck.IsCurrent,
                    UpdatedUtc = truck.UpdatedUtc == default ? DateTime.UtcNow : truck.UpdatedUtc
                });

                return;
            }

            if (string.IsNullOrWhiteSpace(existing.TruckNumber) && !string.IsNullOrWhiteSpace(truck.TruckNumber))
                existing.TruckNumber = truck.TruckNumber.Trim();

            if (string.IsNullOrWhiteSpace(existing.TruckName) && !string.IsNullOrWhiteSpace(truck.TruckName))
                existing.TruckName = truck.TruckName.Trim();

            if (string.IsNullOrWhiteSpace(existing.Plate) && !string.IsNullOrWhiteSpace(truck.Plate))
                existing.Plate = truck.Plate.Trim();

            if (string.IsNullOrWhiteSpace(existing.Vin) && !string.IsNullOrWhiteSpace(truck.Vin))
                existing.Vin = truck.Vin.Trim();

            if (string.IsNullOrWhiteSpace(existing.Source) && !string.IsNullOrWhiteSpace(truck.Source))
                existing.Source = truck.Source.Trim();

            existing.IsCurrent = existing.IsCurrent || truck.IsCurrent;
            existing.UpdatedUtc = DateTime.UtcNow;
        }

        private static void MergeStrings(List<string> target, List<string>? source)
        {
            foreach (var item in source ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(item) &&
                    !target.Any(x => Same(x, item)))
                {
                    target.Add(item.Trim());
                }
            }
        }

        private static DriverProfileMaster? Clone(DriverProfileMaster? p)
        {
            if (p == null)
                return null;

            return new DriverProfileMaster
            {
                DiscordUserId = p.DiscordUserId ?? "",
                DiscordName = p.DiscordName ?? "",
                DisplayName = p.DisplayName ?? "",
                PhotoPath = p.PhotoPath ?? "",
                Role = p.Role ?? "",
                Status = p.Status ?? "",
                Location = p.Location ?? "",
                HomeTerminal = p.HomeTerminal ?? "",
                Email = p.Email ?? "",
                Phone = p.Phone ?? "",
                Bio = p.Bio ?? "",
                Notes = p.Notes ?? "",
                CreatedUtc = p.CreatedUtc,
                UpdatedUtc = p.UpdatedUtc,
                ConnectedTrucks = (p.ConnectedTrucks ?? new List<DriverTruckLink>())
                    .Select(t => new DriverTruckLink
                    {
                        TruckNumber = t.TruckNumber ?? "",
                        TruckName = t.TruckName ?? "",
                        Plate = t.Plate ?? "",
                        Vin = t.Vin ?? "",
                        Source = t.Source ?? "",
                        IsCurrent = t.IsCurrent,
                        UpdatedUtc = t.UpdatedUtc
                    })
                    .ToList(),
                Awards = (p.Awards ?? new List<string>()).ToList(),
                Endorsements = (p.Endorsements ?? new List<string>()).ToList(),
                BolLoadNumbers = (p.BolLoadNumbers ?? new List<string>()).ToList()
            };
        }

        private static string ProfileKey(DriverProfileMaster p)
        {
            if (!string.IsNullOrWhiteSpace(p.DiscordUserId))
                return "discord:" + p.DiscordUserId.Trim();

            if (!string.IsNullOrWhiteSpace(p.DiscordName))
                return "discordname:" + p.DiscordName.Trim();

            return "name:" + p.DisplayName.Trim();
        }

        private static bool IsBlankProfile(DriverProfileMaster? p)
        {
            return p == null ||
                   string.IsNullOrWhiteSpace(p.DiscordUserId) &&
                   string.IsNullOrWhiteSpace(p.DiscordName) &&
                   string.IsNullOrWhiteSpace(p.DisplayName);
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

        private static string Clean(string? value) => value?.Trim() ?? "";

        private static bool Same(string? a, string? b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool SameNonBlank(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            !string.IsNullOrWhiteSpace(b) &&
            Same(a, b);
    }

    public sealed class DriverProfileIdentitySnapshot
    {
        public string DriverId { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DiscordName { get; set; } = "";
        public string DisplayName { get; set; } = "";

        public static DriverProfileIdentitySnapshot Current()
        {
            var snap = new DriverProfileIdentitySnapshot();

            try
            {
                var identity = new DiscordIdentityService().LoadOrDefault();
                snap.DiscordUserId = identity?.DiscordUserId ?? "";
                snap.DiscordName = identity?.DiscordUsername ?? "";
            }
            catch
            {
            }

            snap.DisplayName = FirstNonBlank(
                snap.DiscordName,
                EldDriverIdentityResolver.DriverName(),
                EldCurrentUserService.SafeDisplayName());

            snap.DriverId = OverWatchELD.Models.DriverProfile.BuildStableDriverId(
                snap.DiscordUserId,
                snap.DisplayName,
                snap.DiscordName);

            return snap;
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
    }
}
