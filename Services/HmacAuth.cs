using System;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace OverWatchELD.Services
{
    internal static class VtcHmacAuth
    {
        private static readonly ConcurrentDictionary<string, long> _nonces = new();
        private static long _lastCleanup = 0;

        public static string? GetSharedSecret()
        {
            var env = (Environment.GetEnvironmentVariable("OVERWATCHELD_VTC_SECRET") ?? "").Trim();
            return string.IsNullOrWhiteSpace(env) ? null : env;
        }

        public static bool Verify(HttpListenerRequest req, string body)
        {
            var secret = GetSharedSecret();
            if (string.IsNullOrWhiteSpace(secret))
                return true; // HMAC disabled => allow

            try
            {
                var ts = (req.Headers["X-OW-Timestamp"] ?? "").Trim();
                var nonce = (req.Headers["X-OW-Nonce"] ?? "").Trim();
                var sig = (req.Headers["X-OW-Signature"] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(ts) || string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(sig))
                    return false;

                if (!long.TryParse(ts, out var tsNum)) return false;

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (Math.Abs(now - tsNum) > 60) return false;

                CleanupNonces(now);
                if (!_nonces.TryAdd(nonce, now)) return false;

                var bodyHash = Sha256Hex(body ?? "");
                var path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();
                var msg = $"{ts}.{nonce}.{req.HttpMethod.ToUpperInvariant()}.{path}.{bodyHash}";
                var expected = HmacB64(secret, msg);

                return CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected),
                    Encoding.UTF8.GetBytes(sig));
            }
            catch { return false; }
        }

        private static void CleanupNonces(long now)
        {
            if (now - _lastCleanup < 10) return;
            _lastCleanup = now;
            foreach (var kv in _nonces)
                if (now - kv.Value > 120) _nonces.TryRemove(kv.Key, out _);
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
    }
}