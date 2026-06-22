using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class LoadTrackerService
    {
        public static LoadTrackerService Shared { get; } = new();

        private readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly object _sync = new();

        private ActiveLoadState? _active;
        private bool _lastHadLoad;

        private string ActiveLoadPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD",
                "active_load.json");

        private string LogPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD",
                "load_tracker.log");

        private LoadTrackerService()
        {
            TryLoadState();
            _lastHadLoad = _active != null;
        }

        public void OnTelemetry(TelemetrySnapshot snap)
        {
            if (snap == null)
                return;

            lock (_sync)
            {
                var cargoName = GetBestCargoName(snap);
                var hasLoad = HasRealLoad(snap);

                if (!_lastHadLoad && hasLoad)
                {
                    if (_active == null)
                        CreateNewLoad(snap, cargoName);
                }
                else if (_lastHadLoad && !hasLoad)
                {
                    if (_active != null)
                        CompleteActiveLoad(snap);
                }

                _lastHadLoad = hasLoad;

                if (_active != null && hasLoad)
                {
                    _active.LastSeenUtc = DateTimeOffset.UtcNow;
                    _active.LastLocation = FormatLocation(snap.City, snap.State, _active.LastLocation);
                    _active.Driver = string.IsNullOrWhiteSpace(snap.DriverName) ? _active.Driver : snap.DriverName!.Trim();
                    _active.Truck = string.IsNullOrWhiteSpace(snap.TruckName) ? _active.Truck : snap.TruckName!.Trim();

                    var betterCargo = GetBestCargoName(snap);
                    if (!string.IsNullOrWhiteSpace(betterCargo))
                        _active.Cargo = betterCargo;

                    var weight = GetBestWeight(snap);
                    if (weight > 0)
                        _active.WeightLbs = weight;

                    var revenue = ParseRevenue(snap.RevenueDisplay);
                    if (revenue > 0)
                    {
                        _active.RevenueUsd = revenue;
                        _active.RevenueDisplay = string.IsNullOrWhiteSpace(snap.RevenueDisplay) ? revenue.ToString("C2") : snap.RevenueDisplay.Trim();
                    }

                    if (snap.PlannedMiles.HasValue && snap.PlannedMiles.Value > 0)
                        _active.PlannedMiles = snap.PlannedMiles.Value;

                    if (snap.DamagePct.HasValue)
                        _active.LastTruckDamagePct = NormalizePercent(snap.DamagePct.Value);

                    if (snap.TrailerDamagePct.HasValue)
                        _active.LastTrailerDamagePct = NormalizePercent(snap.TrailerDamagePct.Value);

                    SaveState();
                }
            }
        }

        private void CreateNewLoad(TelemetrySnapshot snap, string cargoName)
        {
            var weight = GetBestWeight(snap);

            _active = new ActiveLoadState
            {
                LoadNumber = GenerateLoadNumber(),
                Driver = string.IsNullOrWhiteSpace(snap.DriverName) ? "Driver" : snap.DriverName!.Trim(),
                Truck = string.IsNullOrWhiteSpace(snap.TruckName) ? "Truck" : snap.TruckName!.Trim(),
                Cargo = string.IsNullOrWhiteSpace(cargoName) ? "Active Load" : cargoName,
                WeightLbs = weight,
                RevenueUsd = ParseRevenue(snap.RevenueDisplay),
                RevenueDisplay = string.IsNullOrWhiteSpace(snap.RevenueDisplay) ? "" : snap.RevenueDisplay.Trim(),
                PlannedMiles = snap.PlannedMiles ?? 0,
                LastTruckDamagePct = snap.DamagePct.HasValue ? NormalizePercent(snap.DamagePct.Value) : 0,
                LastTrailerDamagePct = snap.TrailerDamagePct.HasValue ? NormalizePercent(snap.TrailerDamagePct.Value) : 0,
                StartLocation = FormatLocation(snap.City, snap.State, "Unknown"),
                LastLocation = FormatLocation(snap.City, snap.State, "Unknown"),
                StartUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow
            };

            SaveState();
            Log($"CREATE load={_active.LoadNumber} driver={_active.Driver} truck={_active.Truck} cargo={_active.Cargo} weight={_active.WeightLbs}");

            _ = BolDiscordOnlyService.Shared.PostAsync(
                _active.LoadNumber,
                _active.Driver,
                _active.Truck,
                _active.Cargo,
                _active.WeightLbs,
                _active.StartLocation,
                "",
                "Picked Up");

            _ = NotifyPickupAsync(_active);
        }

        private void CompleteActiveLoad(TelemetrySnapshot snap)
        {
            if (_active == null)
                return;

            var completed = new LoadCompletedDto
            {
                LoadNumber = _active.LoadNumber,
                Driver = _active.Driver,
                Truck = _active.Truck,
                Cargo = _active.Cargo,
                Weight = _active.WeightLbs,
                RevenueUsd = _active.RevenueUsd > 0 ? _active.RevenueUsd : ParseRevenue(snap.RevenueDisplay),
                RevenueDisplay = !string.IsNullOrWhiteSpace(_active.RevenueDisplay) ? _active.RevenueDisplay : (snap.RevenueDisplay ?? ""),
                PlannedMiles = _active.PlannedMiles > 0 ? _active.PlannedMiles : (snap.PlannedMiles ?? 0),
                TruckDamagePct = snap.DamagePct.HasValue ? NormalizePercent(snap.DamagePct.Value) : _active.LastTruckDamagePct,
                TrailerDamagePct = snap.TrailerDamagePct.HasValue ? NormalizePercent(snap.TrailerDamagePct.Value) : _active.LastTrailerDamagePct,
                StartLocation = _active.StartLocation,
                EndLocation = FormatLocation(snap.City, snap.State, _active.LastLocation),
                StartUtc = _active.StartUtc.UtcDateTime,
                EndUtc = DateTimeOffset.UtcNow.UtcDateTime
            };

            Log($"COMPLETE load={completed.LoadNumber} driver={completed.Driver} truck={completed.Truck} cargo={completed.Cargo}");

            _ = BolDiscordOnlyService.Shared.PostAsync(
                completed.LoadNumber,
                completed.Driver,
                completed.Truck,
                completed.Cargo,
                completed.Weight,
                completed.StartLocation,
                completed.EndLocation,
                "Completed");

            _ = NotifyCompletedAsync(completed);

            _active = null;
            DeleteState();
        }

        private async Task NotifyPickupAsync(ActiveLoadState load)
        {
            try
            {
                var baseUrl = GetBotBaseUrl();
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    Log("PICKUP skipped: BotApiBaseUrl empty");
                    return;
                }

                var payload = new
                {
                    loadNumber = load.LoadNumber,
                    currentLoadNumber = load.LoadNumber,
                    loadNo = load.LoadNumber,
                    driver = load.Driver,
                    driverName = load.Driver,
                    truck = load.Truck,
                    truckName = load.Truck,
                    cargo = load.Cargo,
                    commodity = load.Cargo,
                    weight = load.WeightLbs,
                    revenue = load.RevenueUsd,
                    revenueUsd = load.RevenueUsd,
                    revenueDisplay = load.RevenueUsd > 0 ? load.RevenueUsd.ToString("C2") : load.RevenueDisplay,
                    plannedMiles = load.PlannedMiles,
                    truckDamagePct = load.LastTruckDamagePct,
                    trailerDamagePct = load.LastTrailerDamagePct,
                    startLocation = load.StartLocation,
                    origin = load.StartLocation,
                    endLocation = "",
                    destination = "",
                    startUtc = load.StartUtc.UtcDateTime
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null,
                    WriteIndented = false
                });

                Log("PICKUP request: " + json);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _http.PostAsync($"{baseUrl.TrimEnd('/')}/api/loads/pickup", content);
                var body = await resp.Content.ReadAsStringAsync();

                Log($"PICKUP response: HTTP {(int)resp.StatusCode} {body}");

                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {body}");
            }
            catch (Exception ex)
            {
                Log("PICKUP failed: " + ex);
            }
        }

        private async Task NotifyCompletedAsync(LoadCompletedDto dto)
        {
            try
            {
                var baseUrl = GetBotBaseUrl();
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    Log("COMPLETE skipped: BotApiBaseUrl empty");
                    return;
                }

                var payload = new
                {
                    loadNumber = dto.LoadNumber,
                    currentLoadNumber = dto.LoadNumber,
                    loadNo = dto.LoadNumber,
                    driver = dto.Driver,
                    driverName = dto.Driver,
                    truck = dto.Truck,
                    truckName = dto.Truck,
                    cargo = dto.Cargo,
                    commodity = dto.Cargo,
                    weight = dto.Weight,
                    revenue = dto.RevenueUsd,
                    revenueUsd = dto.RevenueUsd,
                    revenueDisplay = dto.RevenueUsd > 0 ? dto.RevenueUsd.ToString("C2") : dto.RevenueDisplay,
                    plannedMiles = dto.PlannedMiles,
                    miles = dto.PlannedMiles,
                    truckDamagePct = dto.TruckDamagePct,
                    trailerDamagePct = dto.TrailerDamagePct,
                    startLocation = dto.StartLocation,
                    origin = dto.StartLocation,
                    endLocation = dto.EndLocation,
                    destination = dto.EndLocation,
                    startUtc = dto.StartUtc,
                    endUtc = dto.EndUtc
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null,
                    WriteIndented = false
                });

                Log("COMPLETE request: " + json);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _http.PostAsync($"{baseUrl.TrimEnd('/')}/api/loads/complete", content);
                var body = await resp.Content.ReadAsStringAsync();

                Log($"COMPLETE response: HTTP {(int)resp.StatusCode} {body}");

                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {body}");
            }
            catch (Exception ex)
            {
                Log("COMPLETE failed: " + ex);
            }
        }


        private static decimal ParseRevenue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0m;

            var chars = new System.Collections.Generic.List<char>();
            foreach (var c in value)
            {
                if (char.IsDigit(c) || c == '.' || c == '-')
                    chars.Add(c);
            }

            var text = new string(chars.ToArray());
            return decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var result)
                ? Math.Abs(result)
                : 0m;
        }

        private static double NormalizePercent(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (value <= 1.001) value *= 100.0;
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }

        private static bool HasRealLoad(TelemetrySnapshot snap)
        {
            if (snap.CargoWeightLbs.HasValue && snap.CargoWeightLbs.Value > 1000)
                return true;
            if (snap.TrailerWeightLbs.HasValue && snap.TrailerWeightLbs.Value > 5000)
                return true;
            if (snap.GrossWeightLbs.HasValue && snap.GrossWeightLbs.Value > 8000)
                return true;
            return false;
        }

        private static double GetBestWeight(TelemetrySnapshot snap)
        {
            if (snap.CargoWeightLbs.HasValue && snap.CargoWeightLbs.Value > 0)
                return snap.CargoWeightLbs.Value;
            if (snap.TrailerWeightLbs.HasValue && snap.TrailerWeightLbs.Value > 0)
                return snap.TrailerWeightLbs.Value;
            if (snap.GrossWeightLbs.HasValue && snap.GrossWeightLbs.Value > 0)
                return snap.GrossWeightLbs.Value;
            return 0;
        }

        private static string GetBestCargoName(TelemetrySnapshot snap)
        {
            if (snap.CargoWeightLbs.HasValue && snap.CargoWeightLbs.Value > 0)
                return $"Cargo ({snap.CargoWeightLbs.Value:N0} lbs)";
            if (snap.TrailerWeightLbs.HasValue && snap.TrailerWeightLbs.Value > 0)
                return $"Trailer Load ({snap.TrailerWeightLbs.Value:N0} lbs)";
            if (snap.GrossWeightLbs.HasValue && snap.GrossWeightLbs.Value > 0)
                return $"Freight ({snap.GrossWeightLbs.Value:N0} lbs gross)";
            return string.Empty;
        }

        private static string FormatLocation(string? city, string? state, string fallback = "")
        {
            var c = (city ?? "").Trim();
            var s = (state ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(c) && !string.IsNullOrWhiteSpace(s))
                return $"{c}, {s}";
            if (!string.IsNullOrWhiteSpace(c))
                return c;
            if (!string.IsNullOrWhiteSpace(s))
                return s;
            return fallback;
        }

        private static string GenerateLoadNumber()
        {
            var rand = Random.Shared.Next(1000, 9999);
            return $"{DateTime.UtcNow:yyyyMMdd}{rand}";
        }

        private string GetBotBaseUrl()
        {
            try
            {
                var cfg = VtcConfigService.LoadOrCreate();
                return (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
            }
            catch
            {
                return "";
            }
        }

        private void TryLoadState()
        {
            try
            {
                if (!File.Exists(ActiveLoadPath))
                    return;

                var raw = File.ReadAllText(ActiveLoadPath);
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                _active = JsonSerializer.Deserialize<ActiveLoadState>(raw);
            }
            catch (Exception ex)
            {
                Log("TryLoadState failed: " + ex.Message);
            }
        }

        private void SaveState()
        {
            try
            {
                var dir = Path.GetDirectoryName(ActiveLoadPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(
                    ActiveLoadPath,
                    JsonSerializer.Serialize(_active, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
            }
            catch (Exception ex)
            {
                Log("SaveState failed: " + ex.Message);
            }
        }

        private void DeleteState()
        {
            try
            {
                if (File.Exists(ActiveLoadPath))
                    File.Delete(ActiveLoadPath);
            }
            catch (Exception ex)
            {
                Log("DeleteState failed: " + ex.Message);
            }
        }

        private void Log(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private sealed class ActiveLoadState
        {
            public string LoadNumber { get; set; } = "";
            public string Driver { get; set; } = "";
            public string Truck { get; set; } = "";
            public string Cargo { get; set; } = "";
            public double WeightLbs { get; set; }
            public decimal RevenueUsd { get; set; }
            public string RevenueDisplay { get; set; } = "";
            public double PlannedMiles { get; set; }
            public double LastTruckDamagePct { get; set; }
            public double LastTrailerDamagePct { get; set; }
            public string StartLocation { get; set; } = "";
            public string LastLocation { get; set; } = "";
            public DateTimeOffset StartUtc { get; set; }
            public DateTimeOffset LastSeenUtc { get; set; }
        }

        private sealed class LoadCompletedDto
        {
            public string LoadNumber { get; set; } = "";
            public string Driver { get; set; } = "";
            public string Truck { get; set; } = "";
            public string Cargo { get; set; } = "";
            public double Weight { get; set; }
            public decimal RevenueUsd { get; set; }
            public string RevenueDisplay { get; set; } = "";
            public double PlannedMiles { get; set; }
            public double TruckDamagePct { get; set; }
            public double TrailerDamagePct { get; set; }
            public string StartLocation { get; set; } = "";
            public string EndLocation { get; set; } = "";
            public DateTime StartUtc { get; set; }
            public DateTime EndUtc { get; set; }
        }
    }
}
