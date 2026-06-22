using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class VtcLocationPingService
    {
        private readonly BotApiService _botApi;
        private DateTimeOffset _lastPostUtc = DateTimeOffset.MinValue;
        private readonly TimeSpan _minPostInterval = TimeSpan.FromSeconds(2);

        public VtcLocationPingService(BotApiService botApi)
        {
            _botApi = botApi ?? throw new ArgumentNullException(nameof(botApi));
        }

        public void Start()
        {
            // no-op by design
        }

        public void Stop()
        {
            // no-op by design
        }

        public Task<bool> OnTelemetryAsync(object? telemetrySnapshot, object? dutyStateMachine = null, CancellationToken ct = default)
            => PostAsync(telemetrySnapshot, dutyStateMachine, ct);

        public Task<bool> TickAsync(object? telemetrySnapshot, object? dutyStateMachine = null, CancellationToken ct = default)
            => PostAsync(telemetrySnapshot, dutyStateMachine, ct);

        public async Task<bool> PostAsync(object? telemetrySnapshot, object? dutyStateMachine = null, CancellationToken ct = default)
        {
            try
            {
                if (telemetrySnapshot == null)
                    return false;

                var now = DateTimeOffset.UtcNow;
                if ((now - _lastPostUtc) < _minPostInterval)
                    return false;

                // ------------------------------------------------------------
                // Coordinate source priority:
                // 1) Real GPS lon/lat if telemetry provides it
                // 2) ATS world X/Z converted into approximate lon/lat
                // ------------------------------------------------------------
                var gpsLon = ReadNullableDouble(
                    telemetrySnapshot,
                    "GpsLongitude",
                    "Longitude",
                    "Lon",
                    "GpsLon",
                    "MapLongitude");

                var gpsLat = ReadNullableDouble(
                    telemetrySnapshot,
                    "GpsLatitude",
                    "Latitude",
                    "Lat",
                    "GpsLat",
                    "MapLatitude");

                double lon;
                double lat;

                if (IsPlausibleLongitude(gpsLon) && IsPlausibleLatitude(gpsLat))
                {
                    lon = gpsLon!.Value;
                    lat = gpsLat!.Value;
                }
                else
                {
                    var worldX = ReadNullableDouble(
                        telemetrySnapshot,
                        "WorldX",
                        "X",
                        "TruckX",
                        "CoordinateX");

                    var worldZ = ReadNullableDouble(
                        telemetrySnapshot,
                        "WorldZ",
                        "Z",
                        "TruckZ",
                        "CoordinateZ");

                    if (!worldX.HasValue || !worldZ.HasValue)
                        return false;

                    // --------------------------------------------------------
                    // ATS -> approximate USA map conversion
                    // Public-release-safe fallback for real-world web map.
                    //
                    // Your current sample:
                    //   X = -39387.5
                    //   Z = -10781.02
                    //
                    // This conversion is tuned to land trucks in the correct
                    // general ATS region instead of "no drivers found".
                    // --------------------------------------------------------
                    ConvertAtsWorldToApproxLonLat(worldX.Value, worldZ.Value, out lon, out lat);

                    if (!IsPlausibleLongitude(lon) || !IsPlausibleLatitude(lat))
                        return false;
                }

                var ident = TryLoadIdentity();

                var telemetryDriverName =
                    ReadString(telemetrySnapshot, "DriverName", "DisplayName", "UserName");

                var telemetryDriverId =
                    ReadString(telemetrySnapshot, "DriverId", "UserId");

                var pairedName = (ident.discordUsername ?? "").Trim();
                var pairedUserId = (ident.discordUserId ?? "").Trim();

                var driverName =
                    !string.IsNullOrWhiteSpace(pairedName) &&
                    !pairedName.Equals("User", StringComparison.OrdinalIgnoreCase)
                        ? pairedName
                        : !string.IsNullOrWhiteSpace(telemetryDriverName)
                            ? telemetryDriverName!.Trim()
                            : "Driver";

                var driverId =
                    !string.IsNullOrWhiteSpace(pairedUserId)
                        ? pairedUserId
                        : !string.IsNullOrWhiteSpace(telemetryDriverId)
                            ? telemetryDriverId!.Trim()
                            : driverName;

                var speedMps = ReadDouble(
                    telemetrySnapshot,
                    "SpeedMps",
                    "VehicleSpeedMps",
                    "TruckSpeedMps");

                var speedMph =
                    ReadNullableDouble(
                        telemetrySnapshot,
                        "VehicleSpeedMph",
                        "SpeedMph",
                        "TruckSpeedMph",
                        "Speed")
                    ?? (speedMps * 2.2369362920544);

                var ping = new LocationPing
                {
                    DriverId = string.IsNullOrWhiteSpace(driverId) ? "driver" : driverId,
                    DriverName = string.IsNullOrWhiteSpace(driverName) ? "Driver" : driverName,

                    // Bot/map contract still uses x/z, but for the live map:
                    // x = longitude, z = latitude
                    X = lon,
                    Z = lat,

                    SpeedMph = speedMph,
                    HeadingDeg = NormalizeHeading(ReadDouble(telemetrySnapshot, "HeadingDeg", "Heading", "TruckHeading")),
                    DutyStatus = ReadString(dutyStateMachine, "CurrentStatus", "CurrentDutyStatus", "Status", "State")
                };

                var ok = await _botApi.PostLocationPingAsync(ping, ct).ConfigureAwait(false);
                if (ok)
                    _lastPostUtc = now;

                return ok;
            }
            catch
            {
                return false;
            }
        }

        private static void ConvertAtsWorldToApproxLonLat(double worldX, double worldZ, out double lon, out double lat)
        {
            // Approximate ATS fallback projection.
            // Tuned so common ATS map coordinates land in believable western/central U.S. regions.
            //
            // Sample calibration:
            //   worldX = -39387.5
            //   worldZ = -10781.02
            // -> near Colorado / Front Range area
            //
            // You can refine this later if you want tighter ATS-road alignment.

            const double baseLon = -105.27;
            const double baseLat = 40.52;
            const double scaleX = 0.00026;
            const double scaleZ = 0.00013;

            lon = baseLon + ((worldX + 39387.5) * scaleX);
            lat = baseLat + ((worldZ + 10781.02) * scaleZ);

            lon = Clamp(lon, -124.8, -66.5);
            lat = Clamp(lat, 24.0, 49.8);
        }

        private static (string guildId, string discordUserId, string discordUsername) TryLoadIdentity()
        {
            // Prefer the saved pairing first
            try
            {
                var pairing = VtcPairingStore.Load();
                if (pairing != null)
                {
                    var gid = (pairing.GuildId ?? "").Trim();
                    var uid = (pairing.DiscordUserId ?? "").Trim();
                    var uname = (pairing.DiscordUsername ?? "").Trim();

                    if (!string.IsNullOrWhiteSpace(gid) ||
                        !string.IsNullOrWhiteSpace(uid) ||
                        !string.IsNullOrWhiteSpace(uname))
                    {
                        return (gid, uid, uname);
                    }
                }
            }
            catch { }

            // Fallback to legacy identity store if present
            try
            {
                var ident = DiscordIdentityStore.Load();
                return (
                    ident?.GuildId ?? "",
                    ident?.DiscordUserId ?? "",
                    ident?.DiscordUsername ?? ""
                );
            }
            catch
            {
                return ("", "", "");
            }
        }

        private static bool IsPlausibleLongitude(double? value)
            => value.HasValue && value.Value >= -180d && value.Value <= 180d;

        private static bool IsPlausibleLatitude(double? value)
            => value.HasValue && value.Value >= -90d && value.Value <= 90d;

        private static bool IsPlausibleLongitude(double value)
            => value >= -180d && value <= 180d;

        private static bool IsPlausibleLatitude(double value)
            => value >= -90d && value <= 90d;

        private static double NormalizeHeading(double headingDeg)
        {
            if (double.IsNaN(headingDeg) || double.IsInfinity(headingDeg))
                return 0;

            while (headingDeg < 0) headingDeg += 360;
            while (headingDeg >= 360) headingDeg -= 360;
            return headingDeg;
        }

        private static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);

        private static string? ReadString(object? obj, params string[] names)
        {
            if (obj == null) return null;

            foreach (var name in names)
            {
                var pi = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (pi == null) continue;

                var value = pi.GetValue(obj);
                if (value == null) continue;

                var s = value.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            return null;
        }

        private static double ReadDouble(object? obj, params string[] names)
            => ReadNullableDouble(obj, names) ?? 0;

        private static double? ReadNullableDouble(object? obj, params string[] names)
        {
            if (obj == null) return null;

            foreach (var name in names)
            {
                var pi = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (pi == null) continue;

                var value = pi.GetValue(obj);
                if (value == null) continue;

                if (value is double d) return d;
                if (value is float f) return f;
                if (value is decimal m) return (double)m;
                if (value is int i) return i;
                if (value is long l) return l;

                if (double.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
    }
}