using System;
using System.Collections.Generic;
using System.Linq;
using OverWatchELD.Models;
using FleetTruck = OverWatchELD.Models.Fleet.FleetTruck;
using MaintenanceRecord = OverWatchELD.Models.Fleet.MaintenanceRecord;
using FuelRecord = OverWatchELD.Models.Fleet.FuelRecord;
using TollRecord = OverWatchELD.Models.Fleet.TollRecord;
namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetMaintenanceService
    {
        private readonly FleetStore _store;
        private readonly FleetRules _rules;
        private readonly FleetMaintenanceLedgerService _ledger = new();

        private Dictionary<string, FleetTruck> _trucks;

        public FleetMaintenanceService(FleetStore store, FleetRules rules)
        {
            _store = store;
            _rules = rules;
            _trucks = _store.Load();
        }

        public IReadOnlyCollection<FleetTruck> GetAll() => _trucks.Values.ToList();

        public FleetTruck? GetByPlate(string plate)
        {
            if (string.IsNullOrWhiteSpace(plate)) return null;
            _trucks.TryGetValue(plate.Trim(), out var t);
            return t;
        }

        public FleetTruck UpsertTruck(string plate, string makeModel = "", string nickname = "")
        {
            plate = (plate ?? "").Trim();
            if (plate.Length == 0) throw new ArgumentException("Plate required.");

            if (!_trucks.TryGetValue(plate, out var t))
            {
                t = new FleetTruck { Plate = plate };
                _trucks[plate] = t;
            }

            if (!string.IsNullOrWhiteSpace(makeModel)) t.MakeModel = makeModel.Trim();
            if (!string.IsNullOrWhiteSpace(nickname)) t.Nickname = nickname.Trim();

            Save();
            return t;
        }

        public void AssignDriver(string plate, string driver)
        {
            var t = GetByPlate(plate) ?? UpsertTruck(plate);
            t.AssignedDriver = (driver ?? "").Trim();
            Save();
        }

        public void LogService(string plate, string type, decimal cost, string notes)
        {
            var t = GetByPlate(plate) ?? UpsertTruck(plate);

            var rec = new MaintenanceRecord
            {
                DateUtc = DateTimeOffset.UtcNow,
                Type = type ?? "",
                Mileage = t.OdometerMiles,
                Cost = cost,
                Notes = notes ?? ""
            };

            t.Maintenance.Insert(0, rec);
            t.TotalMaintenanceCost += (double)cost;
            t.LastMaintenanceUtc = DateTimeOffset.UtcNow;

            var typeLower = (type ?? "").ToLowerInvariant();
            if (typeLower.Contains("oil")) t.LastOilChangeMiles = t.OdometerMiles;
            if (typeLower.Contains("tire")) t.LastTireServiceMiles = t.OdometerMiles;
            if (typeLower.Contains("major") || typeLower.Contains("engine") || typeLower.Contains("service"))
                t.LastMajorServiceMiles = t.OdometerMiles;

            CreateLedgerEntry(
                t,
                billType: NormalizeServiceBillType(type),
                amount: cost,
                notes: notes,
                vendor: "",
                location: "",
                requiresFollowUp: false,
                dueAtMiles: CalculateNextServiceDueMiles(t, typeLower),
                dueDateUtc: null);

            Save();

            try { FleetAlertHub.Raise(t.Plate, $"Service logged: {rec.Type} @ {rec.Mileage:0} mi"); } catch { }
        }

        public void LogFuelFill(string plate, double fuelPctAfter, decimal cost, string notes)
        {
            var t = GetByPlate(plate) ?? UpsertTruck(plate);

            t.FuelLog.Insert(0, new FuelRecord
            {
                DateUtc = DateTimeOffset.UtcNow,
                OdometerMiles = t.OdometerMiles,
                FuelPctAfter = ClampPct(fuelPctAfter),
                Cost = cost,
                Notes = notes ?? ""
            });

            t.LastFuelFillUtc = DateTimeOffset.UtcNow;
            t.LastFuelUtc = DateTimeOffset.UtcNow;
            t.TotalFuelCost += (double)cost;

            CreateLedgerEntry(
                t,
                billType: "Fuel",
                amount: cost,
                notes: string.IsNullOrWhiteSpace(notes) ? $"Fuel fill logged at {fuelPctAfter:0}%." : notes,
                vendor: "",
                location: "",
                requiresFollowUp: false);

            Save();

            try { FleetAlertHub.Raise(t.Plate, $"Fuel fill logged: {fuelPctAfter:0}% @ {t.OdometerMiles:0} mi"); } catch { }
        }

        public void LogToll(string plate, decimal cost, string notes = "")
        {
            var t = GetByPlate(plate) ?? UpsertTruck(plate);

            if (cost <= 0) return;

            // simple debounce to avoid duplicate calls in the same moment
            if (t.LastTollLoggedUtc != DateTimeOffset.MinValue &&
                Math.Abs((DateTimeOffset.UtcNow - t.LastTollLoggedUtc).TotalSeconds) < 10 &&
                t.LastTollAmount == cost)
            {
                return;
            }

            t.TollLog.Insert(0, new TollRecord
            {
                DateUtc = DateTimeOffset.UtcNow,
                OdometerMiles = t.OdometerMiles,
                Cost = cost,
                Notes = notes ?? ""
            });

            t.TotalTollCost += (double)cost;
            t.LastTollUtc = DateTimeOffset.UtcNow;
            t.LastTollAmount = cost;
            t.LastTollLoggedUtc = DateTimeOffset.UtcNow;

            CreateLedgerEntry(
                t,
                billType: "Toll",
                amount: cost,
                notes: string.IsNullOrWhiteSpace(notes) ? "Toll charge logged." : notes,
                vendor: "",
                location: "",
                requiresFollowUp: false);

            Save();

            try { FleetAlertHub.Raise(t.Plate, $"Toll logged: {cost:C}"); } catch { }
        }

        public void MarkDotInspectionNow(string plate, string notes = "")
        {
            var t = GetByPlate(plate) ?? UpsertTruck(plate);
            t.LastDotInspectionUtc = DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(notes))
            {
                t.Maintenance.Insert(0, new MaintenanceRecord
                {
                    DateUtc = DateTimeOffset.UtcNow,
                    Type = "DOT Inspection",
                    Mileage = t.OdometerMiles,
                    Cost = 0,
                    Notes = notes
                });
            }

            CreateLedgerEntry(
                t,
                billType: "DOT",
                amount: 0,
                notes: string.IsNullOrWhiteSpace(notes) ? "DOT inspection marked complete." : notes,
                vendor: "",
                location: "",
                requiresFollowUp: false,
                dueAtMiles: null,
                dueDateUtc: DateTime.UtcNow.AddDays(_rules.DotInspectionIntervalDays));

            Save();

            try { FleetAlertHub.Raise(t.Plate, "DOT inspection marked complete."); } catch { }
        }

        /// <summary>
        /// Call this from your telemetry poll loop or FleetAutoLoggerService.
        /// Includes auto fuel-fill detection and auto damage ledger entries.
        /// </summary>
        public void UpdateFromTelemetry(
            string plate,
            double odometerMiles,
            double fuelPct,
            double engineDmgPct,
            double transDmgPct,
            double cabinDmgPct,
            double chassisDmgPct,
            double wheelsDmgPct)
        {
            var t = GetByPlate(plate) ?? UpsertTruck(plate);

            var newOdo = Math.Max(t.OdometerMiles, odometerMiles);
            var newFuel = ClampPct(fuelPct);

            // do this before overwriting prior state
            TryAutoFuelFill(t, newOdo, newFuel);
            TryAutoDamageEntry(
                t,
                newOdo,
                ClampPct(engineDmgPct),
                ClampPct(transDmgPct),
                ClampPct(cabinDmgPct),
                ClampPct(chassisDmgPct),
                ClampPct(wheelsDmgPct));

            t.OdometerMiles = newOdo;
            t.FuelPct = newFuel;

            t.EngineDamagePct = ClampPct(engineDmgPct);
            t.TransmissionDamagePct = ClampPct(transDmgPct);
            t.CabinDamagePct = ClampPct(cabinDmgPct);
            t.ChassisDamagePct = ClampPct(chassisDmgPct);
            t.WheelsDamagePct = ClampPct(wheelsDmgPct);

            t.LastTelemetryUtc = DateTimeOffset.UtcNow;

            if (t.LastFuelPctSeen < 0) t.LastFuelPctSeen = newFuel;
            t.LastFuelPctSeen = newFuel;
            if (t.LastFuelOdometerSeen < 0) t.LastFuelOdometerSeen = newOdo;
            t.LastFuelOdometerSeen = newOdo;

            Save();
            TryEmitThresholdToasts(t);
        }

        private void TryAutoFuelFill(FleetTruck t, double odo, double fuelNowPct)
        {
            try
            {
                if (t.LastFuelPctSeen < 0 || t.LastFuelOdometerSeen < 0) return;

                var jump = fuelNowPct - t.LastFuelPctSeen;
                if (jump < _rules.FuelFillDetectJumpPct) return;

                if (t.LastFuelFillUtc != DateTimeOffset.MinValue)
                {
                    var mins = (DateTimeOffset.UtcNow - t.LastFuelFillUtc).TotalMinutes;
                    if (mins < _rules.FuelFillMinMinutesBetweenDetect) return;
                }

                var milesDelta = Math.Abs(odo - t.LastFuelOdometerSeen);
                if (milesDelta < _rules.FuelFillMinMilesSinceLastDetect) return;

                t.FuelLog.Insert(0, new FuelRecord
                {
                    DateUtc = DateTimeOffset.UtcNow,
                    OdometerMiles = odo,
                    FuelPctAfter = fuelNowPct,
                    Cost = 0,
                    Notes = $"Auto-detected fuel fill (+{jump:0}% approx)"
                });

                t.LastFuelFillUtc = DateTimeOffset.UtcNow;
                t.LastFuelUtc = DateTimeOffset.UtcNow;

                CreateLedgerEntry(
                    t,
                    billType: "Fuel",
                    amount: 0,
                    notes: $"Auto-detected fuel fill (+{jump:0}% approx)",
                    vendor: "",
                    location: "",
                    requiresFollowUp: false);

                try { FleetAlertHub.Raise(t.Plate, $"Auto fuel fill detected: {fuelNowPct:0}% (+{jump:0}%)"); } catch { }
            }
            catch { }
        }

        private void TryAutoDamageEntry(
            FleetTruck t,
            double odo,
            double engineDmg,
            double transDmg,
            double cabinDmg,
            double chassisDmg,
            double wheelsDmg)
        {
            try
            {
                var oldMax = new[]
                {
                    t.EngineDamagePct, t.TransmissionDamagePct, t.CabinDamagePct, t.ChassisDamagePct, t.WheelsDamagePct
                }.Max();

                var newMax = new[]
                {
                    engineDmg, transDmg, cabinDmg, chassisDmg, wheelsDmg
                }.Max();

                var increase = newMax - oldMax;
                var thresholdHit = newMax >= _rules.DamageWarnPct;

                if (!thresholdHit || increase < 5)
                    return;

                if (t.LastDamageLedgerUtc != DateTimeOffset.MinValue &&
                    (DateTimeOffset.UtcNow - t.LastDamageLedgerUtc).TotalMinutes < 10)
                {
                    if (newMax <= t.LastDamageAlertMaxPct + 2)
                        return;
                }

                t.TotalRepairCost += 0;
                t.LastRepairUtc = DateTimeOffset.UtcNow;
                t.LastDamageAlertMaxPct = newMax;
                t.LastDamageLedgerUtc = DateTimeOffset.UtcNow;

                CreateLedgerEntry(
                    t,
                    billType: "Damage",
                    amount: 0,
                    notes: $"Auto-detected damage increase. Max damage now {newMax:0}%.",
                    vendor: "",
                    location: "",
                    requiresFollowUp: true);

                try { FleetAlertHub.Raise(t.Plate, $"Damage event logged: {newMax:0}%"); } catch { }
            }
            catch { }
        }

        private double _lastFuelLowToastUtcTicks;
        private double _lastDamageToastUtcTicks;
        private double _lastServiceToastUtcTicks;

        private void TryEmitThresholdToasts(FleetTruck t)
        {
            try
            {
                var nowTicks = DateTimeOffset.UtcNow.UtcTicks;

                if (t.FuelPct <= _rules.FuelLowPct)
                {
                    if (ShouldToast(ref _lastFuelLowToastUtcTicks, nowTicks, minutes: 8))
                        FleetAlertHub.Raise(t.Plate, $"Fuel low: {t.FuelPct:0}%");
                }

                var maxDmg = new[]
                {
                    t.EngineDamagePct, t.TransmissionDamagePct, t.CabinDamagePct, t.ChassisDamagePct, t.WheelsDamagePct
                }.Max();

                if (maxDmg >= _rules.DamageHighPct)
                {
                    if (ShouldToast(ref _lastDamageToastUtcTicks, nowTicks, minutes: 10))
                        FleetAlertHub.Raise(t.Plate, $"HIGH damage: {maxDmg:0}%");
                }

                var alerts = GetAlerts(t);
                if (alerts.Any(a => a.Contains("OVERDUE", StringComparison.OrdinalIgnoreCase) ||
                                    a.Contains("due soon", StringComparison.OrdinalIgnoreCase)))
                {
                    if (ShouldToast(ref _lastServiceToastUtcTicks, nowTicks, minutes: 20))
                        FleetAlertHub.Raise(t.Plate, "Service alert: check Fleet alerts.");
                }
            }
            catch { }
        }

        private static bool ShouldToast(ref double lastTicks, long nowTicks, int minutes)
        {
            try
            {
                if (lastTicks <= 0)
                {
                    lastTicks = nowTicks;
                    return true;
                }

                var last = new DateTimeOffset((long)lastTicks, TimeSpan.Zero);
                var now = new DateTimeOffset(nowTicks, TimeSpan.Zero);

                if ((now - last).TotalMinutes >= minutes)
                {
                    lastTicks = nowTicks;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public IReadOnlyList<string> GetAlerts(FleetTruck t)
        {
            var alerts = new List<string>();

            if (t.FuelPct <= _rules.FuelLowPct)
                alerts.Add($"Fuel low: {t.FuelPct:0}%");

            var maxDmg = new[]
            {
                t.EngineDamagePct, t.TransmissionDamagePct, t.CabinDamagePct, t.ChassisDamagePct, t.WheelsDamagePct
            }.Max();

            if (maxDmg >= _rules.DamageHighPct)
                alerts.Add($"HIGH damage: {maxDmg:0}%");
            else if (maxDmg >= _rules.DamageWarnPct)
                alerts.Add($"Damage: {maxDmg:0}%");

            AddServiceAlerts(alerts, "Oil change", t.OdometerMiles, t.LastOilChangeMiles, _rules.OilChangeIntervalMiles);
            AddServiceAlerts(alerts, "Tire service", t.OdometerMiles, t.LastTireServiceMiles, _rules.TireServiceIntervalMiles);
            AddServiceAlerts(alerts, "Major service", t.OdometerMiles, t.LastMajorServiceMiles, _rules.MajorServiceIntervalMiles);

            if (t.LastDotInspectionUtc == DateTimeOffset.MinValue)
            {
                alerts.Add("DOT inspection: never recorded");
            }
            else
            {
                var due = t.LastDotInspectionUtc.AddDays(_rules.DotInspectionIntervalDays);
                var daysLeft = (due - DateTimeOffset.UtcNow).TotalDays;

                if (daysLeft < 0) alerts.Add($"DOT inspection OVERDUE ({Math.Abs(daysLeft):0}d)");
                else if (daysLeft <= _rules.DotDueSoonDays) alerts.Add($"DOT inspection due soon ({daysLeft:0}d)");
            }

            return alerts;
        }

        private void AddServiceAlerts(List<string> alerts, string label, double currentMiles, double lastServiceMiles, double intervalMiles)
        {
            if (lastServiceMiles <= 0)
            {
                alerts.Add($"{label}: never recorded");
                return;
            }

            var dueAt = lastServiceMiles + intervalMiles;
            var remaining = dueAt - currentMiles;

            if (remaining <= 0)
                alerts.Add($"{label} OVERDUE ({Math.Abs(remaining):0} mi)");
            else if (remaining <= _rules.ServiceDueSoonMiles)
                alerts.Add($"{label} due soon ({remaining:0} mi)");
        }

        private string NormalizeServiceBillType(string type)
        {
            var s = (type ?? "").Trim().ToLowerInvariant();

            if (s.Contains("oil")) return "Oil Change";
            if (s.Contains("tire")) return "Tires";
            if (s.Contains("dot")) return "DOT";
            if (s.Contains("inspect")) return "Inspection";
            if (s.Contains("repair")) return "Repair";
            if (s.Contains("upgrade")) return "Upgrade";

            return "Repair";
        }

        private double? CalculateNextServiceDueMiles(FleetTruck t, string typeLower)
        {
            if (typeLower.Contains("oil"))
                return t.OdometerMiles + _rules.OilChangeIntervalMiles;

            if (typeLower.Contains("tire"))
                return t.OdometerMiles + _rules.TireServiceIntervalMiles;

            if (typeLower.Contains("major") || typeLower.Contains("engine") || typeLower.Contains("service"))
                return t.OdometerMiles + _rules.MajorServiceIntervalMiles;

            return null;
        }

        private void CreateLedgerEntry(
            FleetTruck truck,
            string billType,
            decimal amount,
            string notes,
            string vendor,
            string location,
            bool requiresFollowUp,
            double? dueAtMiles = null,
            DateTime? dueDateUtc = null)
        {
            try
            {
                _ledger.AddOrUpdate(new FleetCostEntry
                {
                    TruckId = truck.Plate ?? "",
                    TruckName = string.IsNullOrWhiteSpace(truck.Nickname) ? truck.MakeModel : truck.Nickname,
                    PlateNumber = truck.Plate ?? "",
                    DateUtc = DateTime.UtcNow,
                    BillType = billType ?? "Custom",
                    Amount = amount,
                    OdometerMiles = truck.OdometerMiles,
                    Vendor = vendor ?? "",
                    Location = location ?? "",
                    Notes = notes ?? "",
                    RequiresFollowUp = requiresFollowUp,
                    IsResolved = false,
                    DueAtMiles = dueAtMiles,
                    DueDateUtc = dueDateUtc
                });
            }
            catch { }
        }

        private void Save() => _store.Save(_trucks);

        private static double ClampPct(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }
    }
}