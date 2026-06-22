using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class DriverPerformanceService
    {
        public static DriverPerformanceService Shared { get; } = new DriverPerformanceService();

        public event Action? MetricsUpdated;

        private readonly PerformanceConfig _cfg;

        private DateTimeOffset? _lastTickUtc;
        private double? _lastSpeedMps;

        private double _idleSeconds;
        private double _drivingSeconds;
        private double _speedingSeconds;

        private int _hardBrakeCount;
        private int _overspeedEvents;
        private int _hosViolationCount;

        private double _idleRunSeconds;
        private bool _wasSpeeding;

        private readonly Dictionary<string, DriverLiveState> _driverStates = new(StringComparer.OrdinalIgnoreCase);

        private DriverPerformanceService()
        {
            _cfg = PerformanceConfig.LoadOrCreate();
            try { InitVtcPerfReporter(); } catch { }
        }

        public void ResetToday()
        {
            _lastTickUtc = null;
            _lastSpeedMps = null;

            _idleSeconds = 0;
            _drivingSeconds = 0;
            _speedingSeconds = 0;

            _hardBrakeCount = 0;
            _overspeedEvents = 0;
            _hosViolationCount = 0;

            _idleRunSeconds = 0;
            _wasSpeeding = false;

            MetricsUpdated?.Invoke();
        }

        public void RecordHosViolation()
        {
            _hosViolationCount++;
            MetricsUpdated?.Invoke();
        }

        public void OnTelemetry(TelemetrySnapshot s)
        {
            if (s == null || !s.Connected)
                return;

            var now = DateTimeOffset.UtcNow;

            if (_lastTickUtc == null)
            {
                _lastTickUtc = now;
                _lastSpeedMps = s.SpeedMps;
            }
            else
            {
                var dt = (now - _lastTickUtc.Value).TotalSeconds;
                if (dt > 0 && dt <= 5)
                {
                    var speedMps = s.SpeedMps;
                    var speedMph = speedMps * 2.2369362920544;

                    if (s.EngineOn)
                    {
                        if (speedMph >= _cfg.DrivingMinMph)
                        {
                            _drivingSeconds += dt;
                            _idleRunSeconds = 0;
                        }
                        else
                        {
                            _idleRunSeconds += dt;
                            if (_idleRunSeconds >= _cfg.IdleQualifySeconds)
                                _idleSeconds += dt;
                        }

                        var isSpeeding = speedMph >= _cfg.SpeedingThresholdMph;
                        if (isSpeeding)
                            _speedingSeconds += dt;

                        if (isSpeeding && !_wasSpeeding)
                            _overspeedEvents++;

                        _wasSpeeding = isSpeeding;

                        if (_lastSpeedMps.HasValue)
                        {
                            var accel = (speedMps - _lastSpeedMps.Value) / dt;
                            if (accel <= -_cfg.HardBrakeDecelMps2 && speedMph >= _cfg.HardBrakeMinMph)
                                _hardBrakeCount++;
                        }
                    }
                    else
                    {
                        _idleRunSeconds = 0;
                        _wasSpeeding = false;
                    }

                    _lastSpeedMps = speedMps;
                }

                _lastTickUtc = now;
            }

            try
            {
                SyncDriverLogistics(s);
                SyncDriverBehaviorStore(s);
                UpdateDriverProfilePerformance(s.DriverId ?? "", GetSnapshot());
            }
            catch
            {
            }

            MetricsUpdated?.Invoke();
        }

        public PerformanceSnapshot GetSnapshot()
        {
            var activeSeconds = _drivingSeconds + _idleSeconds;
            var idlePct = activeSeconds <= 0 ? 0 : (_idleSeconds / activeSeconds) * 100.0;

            return new PerformanceSnapshot
            {
                HardBrakes = _hardBrakeCount,
                OverspeedEvents = _overspeedEvents,
                SpeedingMinutes = (int)Math.Round(_speedingSeconds / 60.0),
                IdleMinutes = (int)Math.Round(_idleSeconds / 60.0),
                IdlePercent = Math.Round(idlePct, 1),
                HosViolations = _hosViolationCount
            };
        }

        private void SyncDriverBehaviorStore(TelemetrySnapshot s)
        {
            var driverId = (s.DriverId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(driverId))
                return;

            var snap = GetSnapshot();

            DriverPerformanceStore.UpdateBehaviorMetrics(
                driverId,
                snap.HardBrakes,
                snap.OverspeedEvents,
                snap.SpeedingMinutes,
                snap.IdleMinutes,
                snap.IdlePercent,
                snap.HosViolations);

            DriverPerformanceStore.UpdateLastSeen(driverId);
        }

        private void SyncDriverLogistics(TelemetrySnapshot s)
        {
            var driverId = (s.DriverId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(driverId))
                return;

            if (!_driverStates.TryGetValue(driverId, out var st))
            {
                st = new DriverLiveState();
                _driverStates[driverId] = st;
            }

            var truck = (s.TruckName ?? s.TruckMakeModel ?? "").Trim();
            var location = BuildLocation(s.City, s.State);
            var hasCargo = (s.CargoWeightLbs ?? 0) > 100.0;

            DriverPerformanceStore.UpdateLastSeen(driverId);

            if (s.OdometerMiles.HasValue)
            {
                if (st.LastOdometerMiles.HasValue)
                {
                    var delta = s.OdometerMiles.Value - st.LastOdometerMiles.Value;

                    if (delta >= 1.0 && delta < 50.0)
                    {
                        DriverPerformanceStore.AddMiles(driverId, delta);

                        DriverHistoryStore.AddEvent(new DriverHistoryEntry
                        {
                            DriverId = driverId,
                            DriverName = s.DriverName ?? "",
                            EventType = "Mileage",
                            Miles = delta,
                            Truck = truck,
                            Location = location,
                            Utc = DateTime.UtcNow
                        });
                    }
                }

                st.LastOdometerMiles = s.OdometerMiles.Value;
            }

            if (!string.IsNullOrWhiteSpace(truck) &&
                !string.Equals(st.LastTruck, truck, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(st.LastTruck))
                {
                    DriverHistoryStore.AddEvent(new DriverHistoryEntry
                    {
                        DriverId = driverId,
                        DriverName = s.DriverName ?? "",
                        EventType = "TruckChanged",
                        Truck = truck,
                        Location = location,
                        Utc = DateTime.UtcNow
                    });
                }

                st.LastTruck = truck;
            }

            if (!string.IsNullOrWhiteSpace(location) &&
                !string.Equals(st.LastLocation, location, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(st.LastLocation))
                {
                    DriverHistoryStore.AddEvent(new DriverHistoryEntry
                    {
                        DriverId = driverId,
                        DriverName = s.DriverName ?? "",
                        EventType = "LocationChanged",
                        Truck = truck,
                        Location = location,
                        Utc = DateTime.UtcNow
                    });
                }

                st.LastLocation = location;
            }

            if (hasCargo && !st.HasActiveLoad)
            {
                st.HasActiveLoad = true;

                DriverHistoryStore.AddEvent(new DriverHistoryEntry
                {
                    DriverId = driverId,
                    DriverName = s.DriverName ?? "",
                    EventType = "LoadPickedUp",
                    Truck = truck,
                    CargoWeightLbs = s.CargoWeightLbs ?? 0,
                    Location = location,
                    Utc = DateTime.UtcNow
                });
            }
            else if (!hasCargo && st.HasActiveLoad)
            {
                st.HasActiveLoad = false;

                DriverPerformanceStore.AddLoad(driverId);

                DriverHistoryStore.AddEvent(new DriverHistoryEntry
                {
                    DriverId = driverId,
                    DriverName = s.DriverName ?? "",
                    EventType = "LoadDelivered",
                    Truck = truck,
                    Location = location,
                    Utc = DateTime.UtcNow
                });
            }
        }

        private static string BuildLocation(string? city, string? state)
        {
            var c = (city ?? "").Trim();
            var s = (state ?? "").Trim();

            if (string.IsNullOrWhiteSpace(c) && string.IsNullOrWhiteSpace(s))
                return "";

            if (string.IsNullOrWhiteSpace(c))
                return s;

            if (string.IsNullOrWhiteSpace(s))
                return c;

            return $"{c}, {s}";
        }

        private void UpdateDriverProfilePerformance(string driverId, PerformanceSnapshot snap)
        {
            try
            {
                driverId = (driverId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(driverId))
                    return;

                var path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Config",
                    "DriverProfiles",
                    $"{driverId}.json");

                Dictionary<string, string> data;
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    data = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                data["HardBrakes"] = snap.HardBrakes.ToString();
                data["OverspeedEvents"] = snap.OverspeedEvents.ToString();
                data["SpeedingMinutes"] = snap.SpeedingMinutes.ToString();
                data["IdleMinutes"] = snap.IdleMinutes.ToString();
                data["IdlePercent"] = snap.IdlePercent.ToString("0.0");
                data["HosViolations"] = snap.HosViolations.ToString();
                data["UpdatedUtc"] = DateTime.UtcNow.ToString("o");

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }

        private sealed class DriverLiveState
        {
            public double? LastOdometerMiles { get; set; }
            public string LastTruck { get; set; } = "";
            public string LastLocation { get; set; } = "";
            public bool HasActiveLoad { get; set; }
        }

        private sealed class PerformanceConfig
        {
            public double DrivingMinMph { get; set; } = 3.0;
            public double IdleQualifySeconds { get; set; } = 60.0;
            public double SpeedingThresholdMph { get; set; } = 75.0;
            public double HardBrakeDecelMps2 { get; set; } = 3.0;
            public double HardBrakeMinMph { get; set; } = 15.0;

            public static PerformanceConfig LoadOrCreate()
            {
                try
                {
                    var dir = AppDomain.CurrentDomain.BaseDirectory;
                    var path = Path.Combine(dir, "performance.config.json");

                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        var cfg = JsonSerializer.Deserialize<PerformanceConfig>(json);
                        if (cfg != null) return cfg;
                    }

                    var created = new PerformanceConfig();
                    File.WriteAllText(path, JsonSerializer.Serialize(created, new JsonSerializerOptions { WriteIndented = true }));
                    return created;
                }
                catch
                {
                    return new PerformanceConfig();
                }
            }
        }

        private readonly System.Timers.Timer _vtcPerfTimer = new System.Timers.Timer(20000) { AutoReset = true, Enabled = true };
        private static readonly HttpClient _vtcHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private void InitVtcPerfReporter()
        {
            _vtcPerfTimer.Elapsed += async (_, __) => { try { await TryPostToBotAsync().ConfigureAwait(false); } catch { } };
            _vtcPerfTimer.Start();
        }

        private async System.Threading.Tasks.Task TryPostToBotAsync()
        {
            var cfg = VtcConfigService.Load();
            if (!cfg.Enabled) return;

            var link = VtcLinkService.GetLink();
            if (!link.Linked) return;

            var snap = GetSnapshot();
            var baseUrl = (cfg.BotApiBaseUrl ?? "").TrimEnd('/');

            var payload = new
            {
                driverId = link.DriverKey,
                driverName = link.DriverName,
                vtc = cfg.VtcShort ?? "",
                hardBrakes = snap.HardBrakes,
                idleSeconds = (int)(snap.IdleMinutes * 60.0),
                speedingSeconds = (int)(snap.SpeedingMinutes * 60.0),
                hosViolations = snap.HosViolations
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            using var resp = await _vtcHttp.PostAsync(baseUrl + "/api/perf/upsert", new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            _ = resp.IsSuccessStatusCode;
        }

        public sealed class PerformanceSnapshot
        {
            public int HardBrakes { get; init; }
            public int OverspeedEvents { get; init; }
            public int SpeedingMinutes { get; init; }
            public int IdleMinutes { get; init; }
            public double IdlePercent { get; init; }
            public int HosViolations { get; init; }
        }
    }
}