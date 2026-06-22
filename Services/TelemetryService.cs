using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public sealed class TelemetrySnapshot
    {
        public bool EngineOn { get; init; }
        public bool ParkingBrakeOn { get; init; }
        public double SpeedMps { get; init; }

        public string? City { get; init; }
        public string? State { get; init; }
        public string? TruckMakeModel { get; init; }

        public string? DriverId { get; init; }
        public string? DriverName { get; init; }
        public string? TruckId { get; init; }
        public string? TruckName { get; init; }

        public double? OdometerMiles { get; init; }
        public double? FuelGallons { get; init; }
        public double? FuelCapacityGallons { get; init; }
        public double? FuelPct { get; init; }
        public double? DamagePct { get; init; }
        public double? TrailerDamagePct { get; init; }

        public double? CargoWeightLbs { get; init; }
        public double? TrailerWeightLbs { get; init; }
        public double? GrossWeightLbs { get; init; }

        public DateTimeOffset? GameTimeUtc { get; init; }
        public double? GameTimeScale { get; init; }

        public bool Connected { get; init; }
        public string Source { get; init; } = "None";
        public DateTimeOffset SeenUtc { get; init; }

        public string? SourceCity { get; init; }
        public string? SourceCompany { get; init; }
        public string? DestinationCity { get; init; }
        public string? DestinationCompany { get; init; }
        public string? CargoName { get; init; }

        public double? RemainingMiles { get; init; }

        public double? PlannedMiles { get; init; }

        public string? RevenueDisplay { get; init; }

        public string? TrailerName { get; init; }

        public double? WorldX { get; init; }
        public double? WorldZ { get; init; }
        public double? HeadingDeg { get; init; }

        public double? GpsLatitude { get; init; }
        public double? GpsLongitude { get; init; }

        public double? MarkerX => GpsLongitude ?? WorldX;
        public double? MarkerY => GpsLatitude ?? WorldZ;
        public bool HasMarkerCoordinates => MarkerX.HasValue && MarkerY.HasValue;
    }

    public sealed class TelemetryService
    {
        private readonly TelemetryDutyAutoService _autoDuty = new();
        private readonly TelemetryExpenseMonitorService _expenseMonitor = new();

        private readonly FmcsaComplianceService _fmcsa = new();
        private bool? _lastEngineOn;
        private DateTimeOffset _lastLiveTelemetryPostUtc = DateTimeOffset.MinValue;

        public bool AutoPostTripOnEngineOff { get; set; } = true;

        private readonly System.Timers.Timer _timer;
        private readonly HttpClient _http = new HttpClient();

        public event Action<TelemetrySnapshot>? Updated;

        public TelemetrySnapshot? LastSnapshot { get; private set; }

        public string? LastRawJson { get; private set; }

        public int PollMs { get; set; } = 250;

        public string NavCity { get; private set; } = "";
        public string NavState { get; private set; } = "";

        public string LocationText =>
            string.IsNullOrWhiteSpace(NavCity) && string.IsNullOrWhiteSpace(NavState)
                ? ""
                : $"{NavCity}, {NavState}".Trim().Trim(',');

        public string EndpointUrl { get; set; } = "http://localhost:25555/api/ats/telemetry";

        private volatile bool _polling;

        public TelemetryService()
        {
            _timer = new System.Timers.Timer(PollMs);
            _timer.AutoReset = true;
            _timer.Elapsed += async (_, __) => await PollAsync();
        }

        public void Start()
        {
            _timer.Interval = PollMs;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();

            try
            {
                FleetAutoLoggerService.FlushNow();
            }
            catch
            {
            }
        }

        private async System.Threading.Tasks.Task PollAsync()
        {
            if (_polling) return;
            _polling = true;

            try
            {
                string BuildUrl(string baseUrl)
                {
                    return baseUrl.Contains("?")
                        ? $"{baseUrl}&t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                        : $"{baseUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                }

                string? json = null;
                string usedEndpoint = EndpointUrl;

                try
                {
                    using var resp = await _http.GetAsync(BuildUrl(EndpointUrl));
                    if (resp.IsSuccessStatusCode)
                        json = await resp.Content.ReadAsStringAsync();
                }
                catch
                {
                    json = null;
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    var alt = TrySwapAtsEtsEndpoint(EndpointUrl);

                    if (!string.IsNullOrWhiteSpace(alt) &&
                        !string.Equals(alt, EndpointUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var resp2 = await _http.GetAsync(BuildUrl(alt));
                            if (resp2.IsSuccessStatusCode)
                            {
                                json = await resp2.Content.ReadAsStringAsync();
                                usedEndpoint = alt;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                EldClock.ClearGameTime();
                var app0 = System.Windows.Application.Current as OverWatchELD.App;

                var sessionDriver =
                    (app0?.Session?.DriverName ?? "").Trim();

                string fleetAssignedDriver = "";

                try
                {
                    var fleetStore = new OverWatchELD.Services.Fleet.FleetCommandStore();

                    var activeTruck = fleetStore.LoadAll()
                        .FirstOrDefault(t =>
                            !string.IsNullOrWhiteSpace(t.AssignedDriver) &&
                            !string.Equals(t.AssignedDriver, "Driver", StringComparison.OrdinalIgnoreCase));

                    if (activeTruck != null)
                        fleetAssignedDriver = (activeTruck.AssignedDriver ?? "").Trim();
                }
                catch
                {
                }

                var driverName0 =
                    !string.IsNullOrWhiteSpace(fleetAssignedDriver)
                        ? fleetAssignedDriver
                        : sessionDriver;

                if (string.IsNullOrWhiteSpace(driverName0))
                    driverName0 = "Unknown Driver";

                var driverId0 = MakeStableIdFromName(driverName0);

                if (string.IsNullOrWhiteSpace(json))
                {
                    LastRawJson = null;

                    LastSnapshot = new TelemetrySnapshot
                    {
                        Connected = false,
                        Source = "Funbit:25555",
                        EngineOn = false,
                        ParkingBrakeOn = false,
                        SpeedMps = 0,
                        DriverId = driverId0,
                        DriverName = driverName0,
                        SeenUtc = DateTimeOffset.UtcNow
                    };

                    try
                    {
                        var app = System.Windows.Application.Current as OverWatchELD.App;
                        var duty = app?.DutyMachine;

                        if (duty != null)
                            _fmcsa.OnTelemetryMissing(duty);
                    }
                    catch
                    {
                    }

                    Updated?.Invoke(LastSnapshot);

                    try
                    {
                        await TryPostLiveTelemetryToBotAsync(LastSnapshot);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[TELEMETRY POST] Offline heartbeat error: " + ex.Message);
                    }

                    return;
                }

                if (!string.Equals(usedEndpoint, EndpointUrl, StringComparison.OrdinalIgnoreCase))
                    EndpointUrl = usedEndpoint;

                LastRawJson = json;

                try
                {
                    using var probeDoc = JsonDocument.Parse(json);

                    if (!HasUsefulTruckData(probeDoc.RootElement))
                    {
                        var alt2 = TrySwapAtsEtsEndpoint(usedEndpoint);

                        if (!string.IsNullOrWhiteSpace(alt2) &&
                            !string.Equals(alt2, usedEndpoint, StringComparison.OrdinalIgnoreCase))
                        {
                            using var resp3 = await _http.GetAsync(BuildUrl(alt2));

                            if (resp3.IsSuccessStatusCode)
                            {
                                var json2 = await resp3.Content.ReadAsStringAsync();

                                if (!string.IsNullOrWhiteSpace(json2))
                                {
                                    using var probeDoc2 = JsonDocument.Parse(json2);

                                    if (HasUsefulTruckData(probeDoc2.RootElement))
                                    {
                                        json = json2;
                                        usedEndpoint = alt2;
                                        EndpointUrl = alt2;
                                        LastRawJson = json2;
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool gameConnected = TryGetBool(root, "game", "connected") ?? false;
                bool connected = gameConnected || HasUsefulTruckData(root);

                DateTimeOffset? gameTime = null;
                var gameTimeStr = TryGetString(root, "game", "time");

                if (!string.IsNullOrWhiteSpace(gameTimeStr) &&
                    DateTimeOffset.TryParse(gameTimeStr, out var dto))
                {
                    gameTime = dto.ToUniversalTime();
                }

                if (gameTime.HasValue)
                {
                    EldClock.SetGameTime(gameTime.Value);

                    var gameDay = DateOnly.FromDateTime(
                        gameTime.Value.LocalDateTime);

                    EldClock.MarkActivityForDay(gameDay);
                }

                double? timeScale = TryGetDouble(root, "game", "timeScale");

                bool engineOn =
                    TryGetBool(root, "truck", "engineOn")
                    ?? TryGetBool(root, "truck", "engine", "on")
                    ?? TryGetBool(root, "truck", "engine", "enabled")
                    ?? TryGetBool(root, "truck", "engineRunning")
                    ?? TryGetBool(root, "truck", "engine", "running")
                    ?? false;

                bool parkingBrake =
                    TryGetBool(root, "truck", "parkBrakeOn")
                    ?? TryGetBool(root, "truck", "parkingBrakeOn")
                    ?? TryGetBool(root, "truck", "parkingBrake")
                    ?? false;

                try
                {
                    if (AutoPostTripOnEngineOff && _lastEngineOn == true && engineOn == false && connected)
                    {
                        var day = DateOnly.FromDateTime(DateTime.Now);
                        var existing = InspectionStore.LoadMostRecent(day);
                        InspectionLog? existingLog = existing.HasValue ? existing.Value.Log : null;

                        if (existingLog == null || !existingLog.PostTripCompleted)
                        {
                            var log = existingLog ?? InspectionLog.CreateDefault(day, loadId: null);
                            log.PostTripCompleted = true;
                            log.UpdatedUtc = DateTimeOffset.UtcNow;
                            log.PostTripSignedAtUtc ??= DateTimeOffset.UtcNow;
                            log.PostTripDriverSignatureName ??=
                                (System.Windows.Application.Current as OverWatchELD.App)?.Session?.DriverName;

                            InspectionStore.Save(log);
                        }
                    }

                    _lastEngineOn = engineOn;
                }
                catch
                {
                }

                double rawSpeed =
                    TryGetDouble(root, "truck", "speed")
                    ?? TryGetDouble(root, "truck", "speedKmh")
                    ?? TryGetDouble(root, "truck", "speed_kmh")
                    ?? TryGetDouble(root, "truck", "speedMps")
                    ?? TryGetDouble(root, "truck", "speed_mps")
                    ?? 0.0;

                double speedMps = Math.Abs(rawSpeed) > 70.0 ? rawSpeed / 3.6 : rawSpeed;
                double speedMph = Math.Abs(speedMps * 2.23694);

                string? curCity =
                    TryGetString(root, "truck", "navigation", "currentCity")
                    ?? TryGetString(root, "truck", "navigation", "nearestCity")
                    ?? TryGetString(root, "navigation", "currentCity")
                    ?? TryGetString(root, "navigation", "nearestCity")
                    ?? TryGetString(root, "navigation", "city")
                    ?? TryGetString(root, "job", "sourceCity")
                    ?? TryGetString(root, "job", "destinationCity");

                string? curState =
                    TryGetString(root, "truck", "navigation", "currentState")
                    ?? TryGetString(root, "navigation", "currentState")
                    ?? TryGetString(root, "navigation", "state");

                if (!string.IsNullOrWhiteSpace(curCity))
                    NavCity = curCity.Trim();

                if (!string.IsNullOrWhiteSpace(curState))
                    NavState = curState.Trim();

                string? sourceCity =
                    TryGetString(root, "job", "sourceCity")
                    ?? TryGetString(root, "job", "source", "city");

                string? sourceCompany =
                    TryGetString(root, "job", "sourceCompany")
                    ?? TryGetString(root, "job", "source", "company")
                    ?? TryGetString(root, "job", "sourceCompanyId")
                    ?? TryGetString(root, "job", "source_company");

                string? destinationCity =
                    TryGetString(root, "job", "destinationCity")
                    ?? TryGetString(root, "job", "destination", "city");

                string? destinationCompany =
                    TryGetString(root, "job", "destinationCompany")
                    ?? TryGetString(root, "job", "destination", "company")
                    ?? TryGetString(root, "job", "destinationCompanyId")
                    ?? TryGetString(root, "job", "destination_company");

                string? cargoName =
                    TryGetString(root, "job", "cargo")
                    ?? TryGetString(root, "job", "cargoName")
                    ?? TryGetString(root, "job", "cargo_name")
                    ?? TryGetString(root, "job", "cargoLocalized");

                // ATS/Funbit navigation.estimatedDistance is normally meters.
                // Example: 1587389 meters = about 987 miles.
                double? remainingMiles =
                    ConvertMetersToMilesIfNeeded(
                        TryGetDouble(root, "navigation", "estimatedDistance")
                        ?? TryGetDouble(root, "navigation", "estimated_distance")
                        ?? TryGetDouble(root, "truck", "navigation", "estimatedDistance")
                        ?? TryGetDouble(root, "truck", "navigation", "estimated_distance")
                    )
                    ?? ConvertKmToMilesIfNeeded(
                        TryGetDouble(root, "job", "remainingDistanceKm")
                        ?? TryGetDouble(root, "job", "remaining_distance_km")
                        ?? TryGetDouble(root, "job", "remainingDistance")
                    );

                double? plannedMiles =
                    ConvertKmToMilesIfNeeded(
                        TryGetDouble(root, "job", "plannedDistanceKm")
                        ?? TryGetDouble(root, "job", "planned_distance_km")
                        ?? TryGetDouble(root, "job", "distanceKm")
                        ?? TryGetDouble(root, "job", "distance")
                    );

                string? revenueDisplay =
                    TryGetString(root, "job", "income")
                    ?? TryGetString(root, "job", "revenue")
                    ?? TryGetString(root, "job", "revenueDisplay");

                double? worldX =
                    TryGetDouble(root, "truck", "worldPlacement", "position", "x")
                    ?? TryGetDouble(root, "truck", "worldPlacement", "x")
                    ?? TryGetDouble(root, "truck", "placement", "position", "x")
                    ?? TryGetDouble(root, "truck", "placement", "x")
                    ?? TryGetDouble(root, "truck", "position", "x")
                    ?? TryGetDouble(root, "truck", "coordinateX")
                    ?? TryGetDouble(root, "truck", "coordinate", "x")
                    ?? TryGetDouble(root, "truck", "coordinates", "x")
                    ?? TryGetDouble(root, "truck", "x");

                double? worldZ =
                    TryGetDouble(root, "truck", "worldPlacement", "position", "z")
                    ?? TryGetDouble(root, "truck", "worldPlacement", "z")
                    ?? TryGetDouble(root, "truck", "placement", "position", "z")
                    ?? TryGetDouble(root, "truck", "placement", "z")
                    ?? TryGetDouble(root, "truck", "position", "z")
                    ?? TryGetDouble(root, "truck", "coordinateZ")
                    ?? TryGetDouble(root, "truck", "coordinate", "z")
                    ?? TryGetDouble(root, "truck", "coordinates", "z")
                    ?? TryGetDouble(root, "truck", "z");

                double? headingDeg =
                    TryGetDouble(root, "truck", "worldPlacement", "orientation", "heading")
                    ?? TryGetDouble(root, "truck", "worldPlacement", "heading")
                    ?? TryGetDouble(root, "truck", "placement", "orientation", "heading")
                    ?? TryGetDouble(root, "truck", "placement", "heading")
                    ?? TryGetDouble(root, "navigation", "gps", "heading")
                    ?? TryGetDouble(root, "navigation", "heading")
                    ?? TryGetDouble(root, "truck", "heading");

                double? gpsLat =
                    TryGetDouble(root, "navigation", "gps", "latitude")
                    ?? TryGetDouble(root, "navigation", "latitude")
                    ?? TryGetDouble(root, "gps", "latitude")
                    ?? TryGetDouble(root, "truck", "gps", "latitude")
                    ?? TryGetDouble(root, "truck", "latitude")
                    ?? TryGetDouble(root, "truck", "lat")
                    ?? TryGetDouble(root, "latitude")
                    ?? TryGetDouble(root, "lat");

                double? gpsLon =
                    TryGetDouble(root, "navigation", "gps", "longitude")
                    ?? TryGetDouble(root, "navigation", "longitude")
                    ?? TryGetDouble(root, "gps", "longitude")
                    ?? TryGetDouble(root, "truck", "gps", "longitude")
                    ?? TryGetDouble(root, "truck", "longitude")
                    ?? TryGetDouble(root, "truck", "lon")
                    ?? TryGetDouble(root, "truck", "lng")
                    ?? TryGetDouble(root, "longitude")
                    ?? TryGetDouble(root, "lon")
                    ?? TryGetDouble(root, "lng");

                // Keep truck and trailer telemetry separated.
                // Some telemetry readers can expose trailer values while the truck name/id is blank.
                // Never let trailer id/name leak into TruckId or TruckName.
                string? trailerId =
                    TryGetString(root, "trailer", "id")
                    ?? TryGetString(root, "trailer", "trailerId")
                    ?? TryGetString(root, "trailer", "unitId")
                    ?? TryGetString(root, "trailer", "configurationId");

                string? trailerName =
                    TryGetString(root, "trailer", "name")
                    ?? TryGetString(root, "trailer", "brand")
                    ?? TryGetString(root, "trailer", "model")
                    ?? TryGetString(root, "trailer", "displayName");

                string? truckMake =
                    TryGetString(root, "truck", "make")
                    ?? TryGetString(root, "truck", "brand")
                    ?? TryGetString(root, "truck", "manufacturer")
                    ?? TryGetString(root, "truck", "manufacturerName");

                string? truckModel =
                    TryGetString(root, "truck", "model")
                    ?? TryGetString(root, "truck", "truckModel")
                    ?? TryGetString(root, "truck", "modelName")
                    ?? TryGetString(root, "truck", "variant");

                string? truckNameRaw =
                    TryGetString(root, "truck", "name")
                    ?? TryGetString(root, "truck", "displayName")
                    ?? TryGetString(root, "truck", "truckName")
                    ?? TryGetString(root, "truck", "vehicleName")
                    ?? TryGetString(root, "truck", "localizedName");

                string? truckId =
                    TryGetString(root, "truck", "licensePlate")
                    ?? TryGetString(root, "truck", "license_plate")
                    ?? TryGetString(root, "truck", "plate")
                    ?? TryGetString(root, "truck", "truckId")
                    ?? TryGetString(root, "truck", "id")
                    ?? TryGetString(root, "truck", "unitId")
                    ?? TryGetString(root, "truck", "configurationId");

                truckId = RejectTrailerValue(truckId, trailerName, trailerId);

                if (string.IsNullOrWhiteSpace(truckId))
                {
                    truckId = RejectTrailerValue(LastSnapshot?.TruckId, trailerName, trailerId);

                    if (string.IsNullOrWhiteSpace(truckId))
                        truckId = connected ? "Connected" : "N/A";
                }

                string? truckMakeModel = CombineClean(
                    RejectTrailerValue(truckMake, trailerName, trailerId),
                    RejectTrailerValue(truckModel, trailerName, trailerId));

                string? truckName = FirstNonBlank(
                    RejectTrailerValue(truckNameRaw, trailerName, trailerId),
                    RejectTrailerValue(truckMakeModel, trailerName, trailerId),
                    RejectTrailerValue(truckModel, trailerName, trailerId),
                    RejectTrailerValue(truckMake, trailerName, trailerId),
                    RejectTrailerValue(truckId, trailerName, trailerId),
                    RejectTrailerValue(LastSnapshot?.TruckName, trailerName, trailerId),
                    connected ? "Connected Truck" : "Unknown Truck");

                // Odometer compatibility pass:
                // Newer SCS/ATS trucks and some telemetry readers expose mileage under
                // different names/paths. The new Volvo has been reported to miss miles
                // when only the old truck.odometer field is checked. This helper checks
                // known ATS/Funbit names first, then falls back to a safe recursive scan.
                double? odometerMiles = TryGetOdometerMiles(root);

                double? fuelGallons = ConvertLitersToGallonsIfNeeded(
                    TryGetDouble(root, "truck", "fuel")
                    ?? TryGetDouble(root, "truck", "fuelLiters")
                    ?? TryGetDouble(root, "truck", "fuel_liters")
                    ?? TryGetDouble(root, "truck", "fuelGallons")
                    ?? TryGetDouble(root, "truck", "fuel_gallons")
                );

                double? fuelCapacityGallons = ConvertLitersToGallonsIfNeeded(
                    TryGetDouble(root, "truck", "fuelCapacity")
                    ?? TryGetDouble(root, "truck", "fuelCapacityLiters")
                    ?? TryGetDouble(root, "truck", "fuel_capacity_liters")
                    ?? TryGetDouble(root, "truck", "fuelCapacityGallons")
                    ?? TryGetDouble(root, "truck", "fuel_capacity_gallons")
                );

                double? fuelPct = null;

                if (fuelGallons.HasValue && fuelCapacityGallons.HasValue && fuelCapacityGallons.Value > 0)
                    fuelPct = Math.Clamp((fuelGallons.Value / fuelCapacityGallons.Value) * 100.0, 0.0, 100.0);

                // ATS/Funbit exposes actual truck damage as individual wear fields.
                // Use the highest truck wear value so the dashboard shows the real worst damage.
                var truckWearValues = new[]
                {
                    TryGetDouble(root, "truck", "wearChassis"),
                    TryGetDouble(root, "truck", "wearCabin"),
                    TryGetDouble(root, "truck", "wearEngine"),
                    TryGetDouble(root, "truck", "wearTransmission"),
                    TryGetDouble(root, "truck", "wearWheels"),
                    TryGetDouble(root, "truck", "wear_chassis"),
                    TryGetDouble(root, "truck", "wear_cabin"),
                    TryGetDouble(root, "truck", "wear_engine"),
                    TryGetDouble(root, "truck", "wear_transmission"),
                    TryGetDouble(root, "truck", "wear_wheels")
                }
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

                double? damagePct = truckWearValues.Count > 0
                    ? NormalizeDamagePct(truckWearValues.Max())
                    : NormalizeDamagePct(
                        TryGetDouble(root, "truck", "damage")
                        ?? TryGetDouble(root, "truck", "wear")
                        ?? TryGetDouble(root, "truck", "damagePct")
                        ?? TryGetDouble(root, "truck", "damage_pct")
                    );

                // Trailer damage can be reported several ways depending on the telemetry reader.
                var trailerWearValues = new[]
                {
                    TryGetDouble(root, "trailer", "wearChassis"),
                    TryGetDouble(root, "trailer", "wearBody"),
                    TryGetDouble(root, "trailer", "wearWheels"),
                    TryGetDouble(root, "trailer", "wearCargo"),
                    TryGetDouble(root, "trailer", "wear_chassis"),
                    TryGetDouble(root, "trailer", "wear_body"),
                    TryGetDouble(root, "trailer", "wear_wheels"),
                    TryGetDouble(root, "trailer", "wear_cargo")
                }
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

                double? trailerDamagePct = trailerWearValues.Count > 0
                    ? NormalizeDamagePct(trailerWearValues.Max())
                    : NormalizeDamagePct(
                        TryGetDouble(root, "trailer", "damage")
                        ?? TryGetDouble(root, "trailer", "wear")
                        ?? TryGetDouble(root, "trailer", "damagePct")
                        ?? TryGetDouble(root, "trailer", "damage_pct")
                        ?? TryGetDouble(root, "job", "cargoDamage")
                        ?? TryGetDouble(root, "job", "cargoDamagePct")
                        ?? TryGetDouble(root, "job", "cargo_damage")
                    );

                double? cargoWeightLbs = ConvertKgToLbsIfNeeded(
                    TryGetDouble(root, "job", "cargoWeight")
                    ?? TryGetDouble(root, "job", "cargoWeightKg")
                    ?? TryGetDouble(root, "job", "cargo_weight_kg")
                    ?? TryGetDouble(root, "job", "cargoWeightLbs")
                    ?? TryGetDouble(root, "job", "cargo_weight_lbs")
                );

                double? trailerWeightLbs = ConvertKgToLbsIfNeeded(
                    TryGetDouble(root, "trailer", "mass")
                    ?? TryGetDouble(root, "trailer", "weight")
                    ?? TryGetDouble(root, "trailer", "weightKg")
                    ?? TryGetDouble(root, "trailer", "weight_kg")
                    ?? TryGetDouble(root, "trailer", "weightLbs")
                    ?? TryGetDouble(root, "trailer", "weight_lbs")
                );

                double? grossWeightLbs = null;

                if (cargoWeightLbs.HasValue || trailerWeightLbs.HasValue)
                    grossWeightLbs = (cargoWeightLbs ?? 0) + (trailerWeightLbs ?? 0);

                var snapshot = new TelemetrySnapshot
                {
                    Connected = connected,
                    Source = usedEndpoint,
                    EngineOn = engineOn,
                    ParkingBrakeOn = parkingBrake,
                    SpeedMps = speedMps,


                    City = string.IsNullOrWhiteSpace(NavCity) ? null : NavCity,
                    State = string.IsNullOrWhiteSpace(NavState) ? null : NavState,

                    WorldX = worldX,
                    WorldZ = worldZ,
                    HeadingDeg = headingDeg,
                    GpsLatitude = gpsLat,
                    GpsLongitude = gpsLon,

                    TruckMakeModel = truckMakeModel,
                    DriverId = driverId0,
                    DriverName = driverName0,
                    TruckId = truckId,
                    TruckName = truckName,

                    GameTimeUtc = gameTime,
                    GameTimeScale = timeScale,

                    OdometerMiles = odometerMiles,
                    FuelGallons = fuelGallons,
                    FuelCapacityGallons = fuelCapacityGallons,
                    FuelPct = fuelPct,
                    DamagePct = damagePct,
                    TrailerDamagePct = trailerDamagePct,

                    CargoWeightLbs = cargoWeightLbs,
                    TrailerWeightLbs = trailerWeightLbs,
                    GrossWeightLbs = grossWeightLbs,

                    SourceCity = CleanOrNull(sourceCity),
                    SourceCompany = CleanOrNull(sourceCompany),
                    DestinationCity = CleanOrNull(destinationCity),
                    DestinationCompany = CleanOrNull(destinationCompany),
                    CargoName = cargoName,
                    RemainingMiles = remainingMiles,
                    PlannedMiles = plannedMiles,
                    RevenueDisplay = revenueDisplay,
                    TrailerName = trailerName,
                    SeenUtc = DateTimeOffset.UtcNow
                };

                LastSnapshot = snapshot;

                try
                {
                    await _expenseMonitor.OnTelemetryAsync(snapshot, LastRawJson);
                }
                catch
                {
                }

                try
                {
                    var app = System.Windows.Application.Current as OverWatchELD.App;
                    var duty = app?.DutyMachine;

                    if (duty != null)
                        _fmcsa.OnTelemetrySnapshot(snapshot, duty);
                }
                catch
                {
                }

                try
                {
                    var app = System.Windows.Application.Current as OverWatchELD.App;
                    var duty = app?.DutyMachine as DutyStateMachine;

                    if (snapshot.Connected &&
    snapshot.GameTimeUtc.HasValue &&
    duty != null)
                    {
                        var gameDay = DateOnly.FromDateTime(
                            snapshot.GameTimeUtc.Value.LocalDateTime);

                        var hasActivity =
                            snapshot.EngineOn ||
                            Math.Abs(snapshot.SpeedMps) > 0.5 ||
                            snapshot.OdometerMiles.HasValue;

                        if (hasActivity)
                        {
                            EldClock.MarkActivityForDay(gameDay);

                            _autoDuty.OnTelemetryTick(
                                speedMph: Math.Abs(snapshot.SpeedMps * 2.23694),
                                engineOn: snapshot.EngineOn,
                                parkingBrake: snapshot.ParkingBrakeOn,
                                gameNowUtc: snapshot.GameTimeUtc.Value,
                                duty: duty
                            );
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    // Keep Fleet Command Center / active truck rows up to date.
                    OverWatchELD.Services.Fleet.TelemetryFleetSyncService.SyncActiveTruckFromTelemetry(snapshot);
                }
                catch
                {
                }

                try
                {
                    // Root fleet stats logger: mileage, fuel used, and damage spikes.
                    OverWatchELD.Services.FleetAutoLoggerService.ProcessTelemetrySnapshot(snapshot);
                }
                catch
                {
                }

                try
                {
                    // Fleet maintenance/truck state logger.
                    OverWatchELD.Services.Fleet.FleetAutoLoggerService.ProcessTelemetrySnapshot(snapshot);
                }
                catch
                {
                }

                try
                {
                    // Legacy/Discord load tracker. This is what posts pickup/complete routes.
                    OverWatchELD.Services.LoadTrackerService.Shared.OnTelemetry(snapshot);
                }
                catch
                {
                }

                try
                {
                    // Load Board tracker/UI state.
                    OverWatchELD.Services.LoadBoard.LoadBoardTelemetryService.Shared.OnTelemetry(snapshot);
                }
                catch
                {
                }

                Updated?.Invoke(snapshot);

                try
                {
                    await TryPostLiveTelemetryToBotAsync(snapshot);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[TELEMETRY POST] Outer error: " + ex.Message);
                }
            }
            catch
            {
            }
            finally
            {
                _polling = false;
            }
        }

        private async System.Threading.Tasks.Task TryPostLiveTelemetryToBotAsync(TelemetrySnapshot snapshot)
        {
            if ((DateTimeOffset.UtcNow - _lastLiveTelemetryPostUtc).TotalSeconds < 3)
                return;

            var botBaseUrl = GetBotApiBaseUrl();
            var guildId = GetGuildId();

            if (string.IsNullOrWhiteSpace(botBaseUrl) || string.IsNullOrWhiteSpace(guildId))
            {
                System.Diagnostics.Debug.WriteLine("[TELEMETRY POST] Skipped: missing botBaseUrl or guildId.");
                return;
            }

            _lastLiveTelemetryPostUtc = DateTimeOffset.UtcNow;

            botBaseUrl = botBaseUrl.Trim().TrimEnd('/');
            guildId = guildId.Trim();

            var driverDiscordUserId = GetDriverDiscordUserId();

            if (string.IsNullOrWhiteSpace(driverDiscordUserId))
                driverDiscordUserId = GetStableDriverKey(snapshot);

            var absSpeedMph = Math.Abs(snapshot.SpeedMps * 2.23694);

            var body = new
            {
                guildId = guildId,
                driverDiscordUserId = driverDiscordUserId,
                driverName = snapshot.DriverName ?? "Driver",
                truckName = snapshot.TruckName ?? snapshot.TruckMakeModel ?? "Truck",
                isOnline = OnlinePresenceService.Load(),
                onlineStatus = OnlinePresenceService.Load() ? "online" : "offline",
                markerX = snapshot.MarkerX,
                markerY = snapshot.MarkerY,
                worldX = snapshot.WorldX,
                worldZ = snapshot.WorldZ,
                longitude = snapshot.GpsLongitude,
                latitude = snapshot.GpsLatitude,

                city = snapshot.City,
                state = snapshot.State,
                status = absSpeedMph >= 2.0 ? "Driving" : snapshot.EngineOn ? "On Duty" : "Stopped",
                speedMph = Math.Round(absSpeedMph, 1),
                fuelPct = snapshot.FuelPct,
                damagePct = snapshot.DamagePct,
                trailerDamagePct = snapshot.TrailerDamagePct,
                sourceCity = snapshot.SourceCity,
                sourceCompany = snapshot.SourceCompany,
                destinationCity = snapshot.DestinationCity,
                destinationCompany = snapshot.DestinationCompany
            };

            var json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{botBaseUrl}/api/telemetry?guildId={Uri.EscapeDataString(guildId)}";

            System.Diagnostics.Debug.WriteLine("[TELEMETRY POST] URL: " + url);
            System.Diagnostics.Debug.WriteLine("[TELEMETRY POST] Body: " + json);

            using var resp = await _http.PostAsync(url, content);
            var respText = await resp.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"[TELEMETRY POST] Response {(int)resp.StatusCode}: {respText}");
        }

        private static string GetDriverDiscordUserId()
        {
            return GetSessionValue(
                "DiscordUserId",
                "DriverDiscordUserId",
                "LinkedDiscordUserId",
                "UserId",
                "DiscordId"
            ) ?? "";
        }

        private static string GetBotApiBaseUrl()
        {
            var fromSession = GetSessionValue("BotApiBaseUrl", "BotBaseUrl", "ApiBaseUrl", "VtcBotApiBaseUrl");

            if (!string.IsNullOrWhiteSpace(fromSession))
                return fromSession.Trim();

            var fromConfig = GetConfigValue(
                new[] { "BotApiBaseUrl" },
                new[] { "ApiBaseUrl" },
                new[] { "VtcBotApiBaseUrl" },
                new[] { "Bot", "ApiBaseUrl" },
                new[] { "bot", "apiBaseUrl" }
            );

            if (!string.IsNullOrWhiteSpace(fromConfig))
                return fromConfig.Trim();

            return "https://overwatcheld.up.railway.app";
        }

        private static string GetGuildId()
        {
            var fromSession = GetSessionValue(
                "GuildId",
                "DiscordGuildId",
                "VtcGuildId",
                "LinkedGuildId",
                "SelectedGuildId",
                "CurrentGuildId",
                "ServerId",
                "DiscordServerId"
            );

            if (!string.IsNullOrWhiteSpace(fromSession) && fromSession.Trim() != "0")
                return fromSession.Trim();

            var fromConfig = GetConfigValue(
                new[] { "Discord", "GuildId" },
                new[] { "discord", "guildId" },
                new[] { "GuildId" },
                new[] { "guildId" },
                new[] { "VtcGuildId" },
                new[] { "vtcGuildId" },
                new[] { "SelectedGuildId" },
                new[] { "selectedGuildId" },
                new[] { "LinkedGuildId" },
                new[] { "linkedGuildId" },
                new[] { "ServerId" },
                new[] { "serverId" },
                new[] { "DiscordServerId" },
                new[] { "discordServerId" }
            );

            if (!string.IsNullOrWhiteSpace(fromConfig) && fromConfig.Trim() != "0")
                return fromConfig.Trim();

            try
            {
                var pairing = VtcPairingStore.Load();
                var pairingGuild = (pairing?.GuildId ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(pairingGuild) && pairingGuild != "0")
                    return pairingGuild;
            }
            catch
            {
            }

            return "";
        }

        private static string GetStableDriverKey(TelemetrySnapshot snapshot)
        {
            var fromSession = GetDriverDiscordUserId();

            if (!string.IsNullOrWhiteSpace(fromSession))
                return fromSession.Trim();

            if (!string.IsNullOrWhiteSpace(snapshot.DriverId))
                return snapshot.DriverId.Trim();

            if (!string.IsNullOrWhiteSpace(snapshot.DriverName))
                return MakeStableIdFromName(snapshot.DriverName);

            return "driver";
        }

        private static string? GetSessionValue(params string[] names)
        {
            try
            {
                var app = System.Windows.Application.Current as OverWatchELD.App;
                var session = app?.Session;

                if (session == null)
                    return null;

                var type = session.GetType();

                foreach (var name in names)
                {
                    var prop = type.GetProperty(name);

                    if (prop == null)
                        continue;

                    var value = prop.GetValue(session)?.ToString();

                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
            catch
            {
            }

            return null;
        }

        private static string? GetConfigValue(params string[][] paths)
        {
            foreach (var file in GetCandidateConfigFiles())
            {
                try
                {
                    if (!System.IO.File.Exists(file))
                        continue;

                    using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
                    var root = doc.RootElement;

                    foreach (var path in paths)
                    {
                        var value = TryGetString(root, path);

                        if (!string.IsNullOrWhiteSpace(value))
                            return value.Trim();
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string[] GetCandidateConfigFiles()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return new[]
            {
                System.IO.Path.Combine(appData, "OverWatchELD", "vtc.config.json"),
                System.IO.Path.Combine(localAppData, "OverWatchELD", "vtc.config.json"),

                System.IO.Path.Combine(appData, "OverWatchELD", "vtc_config.json"),
                System.IO.Path.Combine(appData, "OverWatchELD", "VtcConfig.json"),
                System.IO.Path.Combine(appData, "OverWatchELD", "vtc.json"),

                System.IO.Path.Combine(appData, "OverWatch ELD", "vtc.config.json"),
                System.IO.Path.Combine(appData, "OverWatch ELD", "vtc_config.json"),
                System.IO.Path.Combine(appData, "OverWatch ELD", "VtcConfig.json"),
                System.IO.Path.Combine(appData, "OverWatch ELD", "vtc.json"),

                System.IO.Path.Combine(localAppData, "OverWatchELD", "vtc_config.json"),
                System.IO.Path.Combine(localAppData, "OverWatchELD", "VtcConfig.json"),
                System.IO.Path.Combine(localAppData, "OverWatchELD", "vtc.json"),

                System.IO.Path.Combine(localAppData, "OverWatch ELD", "vtc.config.json"),
                System.IO.Path.Combine(localAppData, "OverWatch ELD", "vtc_config.json"),
                System.IO.Path.Combine(localAppData, "OverWatch ELD", "VtcConfig.json"),
                System.IO.Path.Combine(localAppData, "OverWatch ELD", "vtc.json")
            };
        }

        private static string? TrySwapAtsEtsEndpoint(string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return null;

            if (endpoint.Contains("/api/ats/telemetry", StringComparison.OrdinalIgnoreCase))
                return endpoint.Replace("/api/ats/telemetry", "/api/ets2/telemetry", StringComparison.OrdinalIgnoreCase);

            if (endpoint.Contains("/api/ets2/telemetry", StringComparison.OrdinalIgnoreCase))
                return endpoint.Replace("/api/ets2/telemetry", "/api/ats/telemetry", StringComparison.OrdinalIgnoreCase);

            return null;
        }

        private static bool HasUsefulTruckData(JsonElement root)
        {
            return TryGetString(root, "truck", "make") != null
                || TryGetString(root, "truck", "model") != null
                || TryGetString(root, "truck", "name") != null
                || TryGetDouble(root, "truck", "speed") != null
                || TryGetOdometerMiles(root) != null
                || TryGetDouble(root, "navigation", "gps", "latitude") != null
                || TryGetDouble(root, "truck", "worldPlacement", "position", "x") != null
                || TryGetDouble(root, "truck", "placement", "x") != null
                || TryGetDouble(root, "truck", "placement", "z") != null;
        }

        private static string MakeStableIdFromName(string name)
        {
            var clean = (name ?? "driver").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(clean))
                clean = "driver";

            var sb = new StringBuilder();

            foreach (var c in clean)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
                else if (c == ' ' || c == '_' || c == '-')
                    sb.Append('-');
            }

            var result = sb.ToString().Trim('-');

            return string.IsNullOrWhiteSpace(result)
                ? "driver"
                : result;
        }

        private static string? CombineClean(string? a, string? b)
        {
            a = CleanOrNull(a);
            b = CleanOrNull(b);

            if (a == null && b == null) return null;
            if (a == null) return b;
            if (b == null) return a;

            if (b.StartsWith(a, StringComparison.OrdinalIgnoreCase))
                return b;

            return $"{a} {b}".Trim();
        }

        private static string? FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        private static string? RejectTrailerValue(string? value, string? trailerName, string? trailerId)
        {
            value = CleanOrNull(value);

            if (value == null)
                return null;

            if (!string.IsNullOrWhiteSpace(trailerName) &&
                string.Equals(value.Trim(), trailerName.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;

            if (!string.IsNullOrWhiteSpace(trailerId) &&
                string.Equals(value.Trim(), trailerId.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;

            var v = value.ToLowerInvariant();

            if (v.Contains("trailer") ||
                v.Contains("blackhawk") ||
                v.Contains("etnyre") ||
                v.StartsWith("mo_"))
                return null;

            return value;
        }

        private static string? CleanOrNull(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();

            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }

        private static bool? TryGetBool(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path))
                return null;

            try
            {
                if (el.ValueKind == JsonValueKind.True) return true;
                if (el.ValueKind == JsonValueKind.False) return false;

                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();

                    if (bool.TryParse(s, out var b))
                        return b;

                    if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
                        return i != 0;
                }

                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                    return n != 0;
            }
            catch
            {
            }

            return null;
        }

        private static double? TryGetDouble(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path))
                return null;

            try
            {
                if (el.ValueKind == JsonValueKind.Number)
                    return el.GetDouble();

                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();

                    if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                        return d;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string? TryGetString(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path))
                return null;

            try
            {
                if (el.ValueKind == JsonValueKind.String)
                    return CleanOrNull(el.GetString());

                if (el.ValueKind == JsonValueKind.Number)
                    return el.ToString();

                if (el.ValueKind == JsonValueKind.True)
                    return "true";

                if (el.ValueKind == JsonValueKind.False)
                    return "false";
            }
            catch
            {
            }

            return null;
        }

        private static bool TryGetElement(JsonElement root, out JsonElement element, params string[] path)
        {
            element = root;

            foreach (var part in path)
            {
                if (element.ValueKind != JsonValueKind.Object)
                    return false;

                if (!element.TryGetProperty(part, out element))
                    return false;
            }

            return true;
        }

        private static double? TryGetOdometerMiles(JsonElement root)
        {
            // Values known to already be miles. Do NOT km-convert these.
            var miles =
                TryGetDouble(root, "truck", "odometerMiles")
                ?? TryGetDouble(root, "truck", "odometer_miles")
                ?? TryGetDouble(root, "truck", "odometerMi")
                ?? TryGetDouble(root, "truck", "odometer_mi")
                ?? TryGetDouble(root, "truck", "truckOdometerMiles")
                ?? TryGetDouble(root, "truck", "truck_odometer_miles")
                ?? TryGetDouble(root, "truck", "mileage")
                ?? TryGetDouble(root, "truck", "miles")
                ?? TryGetDouble(root, "dashboard", "odometerMiles")
                ?? TryGetDouble(root, "dashboard", "odometer_miles")
                ?? TryGetDouble(root, "dashboard", "mileage")
                ?? TryGetDouble(root, "odometerMiles")
                ?? TryGetDouble(root, "odometer_miles")
                ?? TryGetDouble(root, "truckOdometerMiles")
                ?? TryGetDouble(root, "truck_odometer_miles")
                ?? TryGetDouble(root, "mileage");

            if (miles.HasValue)
                return Math.Max(0, miles.Value);

            // ATS/Funbit commonly reports odometer in kilometers.
            var km =
                TryGetDouble(root, "truck", "odometer")
                ?? TryGetDouble(root, "truck", "odometerKm")
                ?? TryGetDouble(root, "truck", "odometer_km")
                ?? TryGetDouble(root, "truck", "truckOdometer")
                ?? TryGetDouble(root, "truck", "truck_odometer")
                ?? TryGetDouble(root, "truck", "truckOdometerKm")
                ?? TryGetDouble(root, "truck", "truck_odometer_km")
                ?? TryGetDouble(root, "truck", "odometerDistance")
                ?? TryGetDouble(root, "truck", "odometer_distance")
                ?? TryGetDouble(root, "dashboard", "odometer")
                ?? TryGetDouble(root, "dashboard", "odometerKm")
                ?? TryGetDouble(root, "dashboard", "odometer_km")
                ?? TryGetDouble(root, "odometer")
                ?? TryGetDouble(root, "odometerKm")
                ?? TryGetDouble(root, "odometer_km")
                ?? TryGetDouble(root, "truckOdometer")
                ?? TryGetDouble(root, "truck_odometer")
                ?? TryGetDouble(root, "truckOdometerKm")
                ?? TryGetDouble(root, "truck_odometer_km");

            if (km.HasValue)
                return ConvertKmToMilesIfNeeded(Math.Max(0, km.Value));

            // Last-resort recursive scan for newer truck/telemetry reader naming.
            // Handles fields such as vehicle.odometer, telemetry.truckOdometerKm, etc.
            if (TryFindOdometerValue(root, out var foundValue, out var foundKey))
            {
                if (IsMilesKey(foundKey))
                    return Math.Max(0, foundValue);

                if (IsMetersKey(foundKey))
                    return Math.Max(0, foundValue / 1609.344);

                return ConvertKmToMilesIfNeeded(Math.Max(0, foundValue));
            }

            return null;
        }

        private static bool TryFindOdometerValue(JsonElement element, out double value, out string key)
        {
            value = 0;
            key = "";

            try
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in element.EnumerateObject())
                    {
                        var propName = prop.Name ?? "";
                        var normalized = NormalizeTelemetryKey(propName);

                        if (LooksLikeOdometerKey(normalized) && TryReadDouble(prop.Value, out var direct) && direct >= 0)
                        {
                            value = direct;
                            key = propName;
                            return true;
                        }

                        if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            if (TryFindOdometerValue(prop.Value, out value, out key))
                                return true;
                        }
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        if (TryFindOdometerValue(item, out value, out key))
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryReadDouble(JsonElement element, out double value)
        {
            value = 0;

            try
            {
                if (element.ValueKind == JsonValueKind.Number)
                    return element.TryGetDouble(out value);

                if (element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString();
                    return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                        || double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool LooksLikeOdometerKey(string normalizedKey)
        {
            if (string.IsNullOrWhiteSpace(normalizedKey))
                return false;

            return normalizedKey.Contains("odometer", StringComparison.OrdinalIgnoreCase)
                || normalizedKey.Contains("truckodometer", StringComparison.OrdinalIgnoreCase)
                || normalizedKey.Equals("odo", StringComparison.OrdinalIgnoreCase)
                || normalizedKey.Equals("mileage", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMilesKey(string key)
        {
            var k = NormalizeTelemetryKey(key);
            return k.Contains("mile", StringComparison.OrdinalIgnoreCase)
                || k.EndsWith("mi", StringComparison.OrdinalIgnoreCase)
                || k.Equals("mileage", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMetersKey(string key)
        {
            var k = NormalizeTelemetryKey(key);
            return k.Contains("meter", StringComparison.OrdinalIgnoreCase)
                || k.EndsWith("m", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeTelemetryKey(string? value)
        {
            return (value ?? "")
                .Trim()
                .Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal)
                .Replace(" ", "", StringComparison.Ordinal)
                .ToLowerInvariant();
        }

        private static double? ConvertMetersToMilesIfNeeded(double? value)
        {
            if (!value.HasValue)
                return null;

            return value.Value * 0.000621371;
        }

        private static double? ConvertKmToMilesIfNeeded(double? value)
        {
            if (!value.HasValue)
                return null;

            return value.Value * 0.621371;
        }

        private static double? ConvertLitersToGallonsIfNeeded(double? value)
        {
            if (!value.HasValue)
                return null;

            return value.Value * 0.264172;
        }

        private static double? ConvertKgToLbsIfNeeded(double? value)
        {
            if (!value.HasValue)
                return null;

            return value.Value * 2.20462;
        }

        private static double? NormalizeDamagePct(double? value)
        {
            if (!value.HasValue)
                return null;

            var v = value.Value;

            if (v <= 1.0)
                return Math.Clamp(v * 100.0, 0.0, 100.0);

            return Math.Clamp(v, 0.0, 100.0);
        }
    }
}