using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class EldDriverIdentityResolver
    {
        public static string DriverName()
        {
            return FirstNonBlank(
                FromDiscordIdentity(),
                FromPairingStore(),
                FromVtcConfig(),
                EldCurrentUserService.SafeDisplayName());
        }

        private static string FromDiscordIdentity()
        {
            try
            {
                var identity = DiscordIdentityService.Load();

                return FirstNonBlank(
                    GetProp(identity, "DisplayName"),
                    GetProp(identity, "DiscordUsername"),
                    GetProp(identity, "Username"),
                    GetProp(identity, "Name"),
                    GetProp(identity, "DriverName"));
            }
            catch { return ""; }
        }

        private static string FromPairingStore()
        {
            try
            {
                var pairing = VtcPairingStore.Load();

                return FirstNonBlank(
                    GetProp(pairing, "DriverName"),
                    GetProp(pairing, "DisplayName"),
                    GetProp(pairing, "DiscordUsername"),
                    GetProp(pairing, "Username"),
                    GetProp(pairing, "Name"));
            }
            catch { return ""; }
        }

        private static string FromVtcConfig()
        {
            try
            {
                var cfg = VtcConfigService.Load(true);
                var discord = GetObj(cfg, "Discord");

                return FirstNonBlank(
                    GetProp(cfg, "DriverName"),
                    GetProp(cfg, "DisplayName"),
                    GetProp(cfg, "Username"),
                    GetProp(cfg, "UserName"),
                    GetProp(discord, "DisplayName"),
                    GetProp(discord, "DiscordUsername"),
                    GetProp(discord, "Username"),
                    GetProp(discord, "Name"));
            }
            catch { return ""; }
        }

        private static object? GetObj(object? obj, string name)
        {
            try
            {
                return obj?.GetType().GetProperty(name)?.GetValue(obj);
            }
            catch { return null; }
        }

        private static string GetProp(object? obj, string name)
        {
            try
            {
                return obj?.GetType().GetProperty(name)?.GetValue(obj)?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private static string FirstNonBlank(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();

            return "";
        }
    }
}
