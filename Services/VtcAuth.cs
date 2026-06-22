// Services/VtcAuth.cs
// HMAC auth for bot->ELD write requests.
// Enabled automatically if secret exists:
// - Env: OVERWATCHELD_VTC_SECRET
// (Optionally also tries to find "SharedSecret" on config via reflection.)

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace OverWatchELD.Services
{
    internal static class VtcAuth
    {
        // Nonce replay protection (2 minutes)
        private static readonly ConcurrentDictionary<string, long> _nonces = new();
        private static long _lastCleanup = 0;

        public static string? GetSharedSecret()
        {
            // Primary: environment variable (works immediately, no config changes needed)
            var env = (Environment.GetEnvironmentVariable("OVERWATCHELD_VTC_SECRET") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(env)) return env;

            // Optional: try config reflection so you can add later without breaking builds
            try
            {
                var cfg = VtcConfigService.Get();
                var secret = ReadStringProp(cfg, "SharedSecret")
                             ?? ReadStringProp(cfg, "BotSharedSecret")
                             ?? ReadStringProp(ReadObjProp(cfg, "Security"), "SharedSecret")
                             ?? ReadStringProp(ReadObjProp(cfg, "Security"), "BotSharedSecret");
                secret = (secret ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(secret)) return secret;
            }
            catch { }

            return null;
        }

        public static bool Verify(HttpListenerRequest req, string secret, string body)
        {
            try
            {
                var ts = (req.Headers["X-OW-Timestamp"] ?? "").Trim();
                var nonce = (req.Headers["X-OW-Nonce"] ?? "").Trim();
                var sig = (req.Headers["X-OW-Signature"] ?? "").Trim();

                if (string.IsNullOrWhiteSpace(ts) || string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(sig))
                    return false;

                if (!long.TryParse(ts, out var tsNum)) return false;

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (Math.Abs(now - tsNum) > 60) return false; // 60s skew max

                CleanupNonces(now);
                if (!_nonces.TryAdd(nonce, now)) return false; // replayed

                var bodyHash = Sha256Hex(body ?? "");
                var path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();
                var msg = $"{ts}.{nonce}.{req.HttpMethod.ToUpperInvariant()}.{path}.{bodyHash}";

                var expected = HmacB64(secret, msg);
                return FixedTimeEquals(expected, sig);
            }
            catch { return false; }
        }

        private static void CleanupNonces(long now)
        {
            if (now - _lastCleanup < 10) return; // every 10s
            _lastCleanup = now;

            foreach (var kv in _nonces)
            {
                if (now - kv.Value > 120)
                    _nonces.TryRemove(kv.Key, out _);
            }
        }

        private static string Sha256Hex(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string HmacB64(string secret, string msg)
        {
            var key = Encoding.UTF8.GetBytes(secret);
            var data = Encoding.UTF8.GetBytes(msg);
            var sig = HMACSHA256.HashData(key, data);
            return Convert.ToBase64String(sig);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            var ba = Encoding.UTF8.GetBytes(a ?? "");
            var bb = Encoding.UTF8.GetBytes(b ?? "");
            return CryptographicOperations.FixedTimeEquals(ba, bb);
        }

        private static string? ReadStringProp(object? obj, string prop)
        {
            try
            {
                if (obj == null) return null;
                var pi = obj.GetType().GetProperty(prop);
                if (pi == null) return null;
                return pi.GetValue(obj)?.ToString();
            }
            catch { return null; }
        }

        private static object? ReadObjProp(object? obj, string prop)
        {
            try
            {
                if (obj == null) return null;
                var pi = obj.GetType().GetProperty(prop);
                if (pi == null) return null;
                return pi.GetValue(obj);
            }
            catch { return null; }
        }
    }
}