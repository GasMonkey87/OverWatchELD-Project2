using System;

namespace OverWatchELD.Services.Fleet
{
    public static class FleetPhase2Ingest
    {
        private static double ToPct(double v, bool is0to1)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            if (is0to1) v *= 100.0;
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            return v;
        }

        public static void Update(
            FleetMaintenanceService fleet,
            string plate,
            string makeModel,
            double odometerMiles,
            double fuel, bool fuelIs0to1,
            double engineDamage, bool engineIs0to1,
            double transDamage, bool transIs0to1,
            double cabinDamage, bool cabinIs0to1,
            double chassisDamage, bool chassisIs0to1,
            double wheelsDamage, bool wheelsIs0to1)
        {
            if (fleet == null) return;

            plate = (plate ?? "").Trim();
            if (string.IsNullOrWhiteSpace(plate))
                plate = "UNIT-001";

            makeModel = (makeModel ?? "").Trim();

            fleet.UpsertTruck(plate, makeModel);

            fleet.UpdateFromTelemetry(
                plate: plate,
                odometerMiles: odometerMiles,
                fuelPct: ToPct(fuel, fuelIs0to1),
                engineDmgPct: ToPct(engineDamage, engineIs0to1),
                transDmgPct: ToPct(transDamage, transIs0to1),
                cabinDmgPct: ToPct(cabinDamage, cabinIs0to1),
                chassisDmgPct: ToPct(chassisDamage, chassisIs0to1),
                wheelsDmgPct: ToPct(wheelsDamage, wheelsIs0to1)
            );
        }
    }
}