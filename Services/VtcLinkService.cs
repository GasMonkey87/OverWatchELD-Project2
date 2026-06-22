using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Local (per-PC) persistence for linking THIS ELD instance to a Discord user via a one-time code.
    /// Flow:
    /// 1) ELD Login -> Connect -> GenerateCode()
    /// 2) Driver DMs the bot: !link CODE
    /// 3) Bot calls ELD Companion endpoint: POST /api/vtc/link/confirm with code + discordUserId/name
    /// 4) We persist the link here so it survives restarts.
    /// </summary>
    public static class VtcLinkService
    {
        private static readonly object _lock = new();

        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATS_ELD");

        private static readonly string LinkFile = Path.Combine(Folder, "vtc_link.json");

        private static readonly string PendingFile = Path.Combine(Folder, "vtc_pending.json");

        public sealed class LinkState
        {
            public bool Linked { get; set; } = false;
            public string DriverKey { get; set; } = "";
            public string DriverName { get; set; } = "Driver";

            public string DiscordUserId { get; set; } = "";
            public string DiscordUserName { get; set; } = "";

            public string VtcName { get; set; } = "";
            public string VtcShort { get; set; } = "";

            public DateTimeOffset LinkedUtc { get; set; } = DateTimeOffset.MinValue;
        }

        private sealed class PendingState
        {
            public PendingCode[] Codes { get; set; } = Array.Empty<PendingCode>();
        }

        private sealed class PendingCode
        {
            public string Code { get; set; } = "";
            public string DriverKey { get; set; } = "";
            public string DriverName { get; set; } = "Driver";
            public DateTimeOffset ExpiresUtc { get; set; }
        }

        public static LinkState GetLink()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(LinkFile)) return new LinkState();
                    var json = File.ReadAllText(LinkFile);
                    return JsonSerializer.Deserialize<LinkState>(json, JsonOpts) ?? new LinkState();
                }
                catch { return new LinkState(); }
            }
        }

        public static void SaveLink(LinkState state)
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    var safe = state ?? new LinkState();
                    var json = JsonSerializer.Serialize(safe, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(LinkFile, json);

                    try
                    {
                        if (!safe.Linked)
                        {
                            VtcPairingStore.Clear();
                        }
                    }
                    catch { }
                }
                catch { }
            }
        }

        public static void ClearLink()
        {
            SaveLink(new LinkState());
            try { VtcPairingStore.Clear(); } catch { }
        }

        public static string GetDriverKey(string driverName)
        {
            var n = (driverName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(n)) n = "Driver";
            // stable key across restarts
            return "drv_" + Sha1Hex(n).Substring(0, 12);
        }

        public static string GenerateCode(string driverName, int codeLen = 6, int expiresMinutes = 10)
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Folder);

                var key = GetDriverKey(driverName);
                var code = RandomCode(codeLen);
                var expires = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, expiresMinutes));

                var pending = LoadPending();
                // remove expired + keep newest per driver
                var list = new System.Collections.Generic.List<PendingCode>();
                foreach (var p in pending.Codes)
                {
                    if (p == null) continue;
                    if (p.ExpiresUtc <= DateTimeOffset.UtcNow) continue;
                    if (string.Equals(p.DriverKey, key, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(p);
                }
                list.Add(new PendingCode { Code = code, DriverKey = key, DriverName = (driverName ?? "Driver").Trim(), ExpiresUtc = expires });
                pending.Codes = list.ToArray();
                SavePending(pending);

                return code;
            }
        }

        public static bool TryConsumeCode(string code, out string driverKey, out string driverName, out string error)
        {
            driverKey = "";
            driverName = "Driver";
            error = "invalid_or_expired";

            lock (_lock)
            {
                var c = (code ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(c)) { error = "missing_code"; return false; }

                var pending = LoadPending();
                var now = DateTimeOffset.UtcNow;

                PendingCode? match = null;
                var list = new System.Collections.Generic.List<PendingCode>();

                foreach (var p in pending.Codes)
                {
                    if (p == null) continue;
                    if (p.ExpiresUtc <= now) continue;

                    if (match == null && string.Equals((p.Code ?? "").Trim().ToUpperInvariant(), c, StringComparison.OrdinalIgnoreCase))
                    {
                        match = p;
                        continue; // single-use: remove it
                    }

                    list.Add(p);
                }

                pending.Codes = list.ToArray();
                SavePending(pending);

                if (match == null) { error = "invalid_or_expired"; return false; }

                driverKey = match.DriverKey ?? "";
                driverName = (match.DriverName ?? "Driver").Trim();
                if (string.IsNullOrWhiteSpace(driverKey)) { error = "bad_driver"; return false; }

                error = "";
                return true;
            }
        }

        private static PendingState LoadPending()
        {
            try
            {
                if (!File.Exists(PendingFile)) return new PendingState();
                var json = File.ReadAllText(PendingFile);
                return JsonSerializer.Deserialize<PendingState>(json, JsonOpts) ?? new PendingState();
            }
            catch { return new PendingState(); }
        }

        private static void SavePending(PendingState pending)
        {
            try
            {
                var json = JsonSerializer.Serialize(pending ?? new PendingState(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PendingFile, json);
            }
            catch { }
        }

        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        private static string RandomCode(int len)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            len = Math.Clamp(len, 4, 10);
            Span<byte> bytes = stackalloc byte[len];
            RandomNumberGenerator.Fill(bytes);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++) sb.Append(chars[bytes[i] % chars.Length]);
            return sb.ToString();
        }

        private static string Sha1Hex(string s)
        {
            try
            {
                using var sha = SHA1.Create();
                var bytes = Encoding.UTF8.GetBytes(s ?? "");
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }
    }
}
