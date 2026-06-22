using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OverWatchELD.Models
{
    /// <summary>
    /// Daily / load-specific inspection + compliance log backing the Compliance tab.
    /// IMPORTANT: Do NOT define InspectionChecklistItem here (it already exists elsewhere in the project).
    /// This model only references it.
    /// </summary>
    public class InspectionLog
    {
        // Identity + timestamps
        public string LogId { get; set; } = Guid.NewGuid().ToString("N");
        public DateOnly LocalDay { get; set; } = DateOnly.FromDateTime(DateTime.Now);
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

        // Load context
        public string? LoadId { get; set; }
        public string? LoadNotes { get; set; }
        public string? DotNotes { get; set; }

        // Vehicle context (truck/trailer)
        public string? TruckId { get; set; }
        public string? TrailerId { get; set; }
        public string? LicensePlate { get; set; }
        public string? OdometerMiles { get; set; }
        public string? EngineHours { get; set; }

        // Pre/Post trip
        public bool PreTripCompleted { get; set; }
        public string? PreTripNotes { get; set; }
        public bool PostTripCompleted { get; set; }
        public string? PostTripNotes { get; set; }


        // DVIR-style signatures / acknowledgements (mobile + desktop)
        public string? PreTripDriverSignatureName { get; set; }
        public DateTimeOffset? PreTripSignedAtUtc { get; set; }

        public string? PostTripDriverSignatureName { get; set; }
        public DateTimeOffset? PostTripSignedAtUtc { get; set; }

        public string? CarrierAckName { get; set; }
        public DateTimeOffset? CarrierAckAtUtc { get; set; }

        public string? Remarks { get; set; }

        // Legacy lists (kept for backward compatibility with earlier UI bindings)
        public List<InspectionChecklistItem> PreTrip { get; set; } = new();
        public List<InspectionChecklistItem> PostTrip { get; set; } = new();

        // NEW: Separate tractor + trailer lists (preferred for your updated UI)
        public List<InspectionChecklistItem> TractorPreTrip { get; set; } = new();
        public List<InspectionChecklistItem> TrailerPreTrip { get; set; } = new();
        public List<InspectionChecklistItem> TractorPostTrip { get; set; } = new();
        public List<InspectionChecklistItem> TrailerPostTrip { get; set; } = new();

        
        private static void PrefillIdentityItems(InspectionLog log)
        {
            static void InsertIfMissing(List<InspectionChecklistItem> list, string name, string? value)
            {
                // "Identity" rows that appear at the top of the inspection list (Unit #, Trailer #, etc.)
                // We encode the value into the Name so the UI can show it without needing editable Name.
                if (list.Count > 0 && string.Equals(list[0].Name, name, StringComparison.OrdinalIgnoreCase))
                    return;

                var hasValue = !string.IsNullOrWhiteSpace(value);
                var display = hasValue ? $"{name}: {value}" : name;

                // Key must be stable-ish. Use a sanitized version of the label.
                var key = "identity_" + name.Replace("#", "num").Replace(" ", "_").Replace("/", "_").ToLowerInvariant();

                var item = new InspectionChecklistItem("Identity", display, key)
                {
                    IsOk = hasValue,
                    IsDefect = !hasValue,
                    Note = hasValue ? string.Empty : "Missing"
                };

                list.Insert(0, item);
            }


            // Tractor
            InsertIfMissing(log.PreTripTractorChecklist, "Tractor Unit #", log.TruckId);
            InsertIfMissing(log.PreTripTractorChecklist, "License Plate", log.LicensePlate);

            InsertIfMissing(log.PostTripTractorChecklist, "Tractor Unit #", log.TruckId);
            InsertIfMissing(log.PostTripTractorChecklist, "License Plate", log.LicensePlate);

            // Trailer / load
            InsertIfMissing(log.PreTripTrailerChecklist, "Trailer #", log.TrailerId);
            InsertIfMissing(log.PreTripTrailerChecklist, "Load ID", log.LoadId);

            InsertIfMissing(log.PostTripTrailerChecklist, "Trailer #", log.TrailerId);
            InsertIfMissing(log.PostTripTrailerChecklist, "Load ID", log.LoadId);
        }

public string DisplayName
        {
            get
            {
                var day = LocalDay.ToString("yyyy-MM-dd");
                var load = string.IsNullOrWhiteSpace(LoadId) ? "No Load" : LoadId.Trim();
                return $"{day} • {load}";
            }
        }

        public static InspectionLog CreateDefault()
        {
            var log = new InspectionLog
            {
                LocalDay = DateOnly.FromDateTime(DateTime.Now),
                UpdatedUtc = DateTimeOffset.UtcNow,
                PreTripCompleted = false,
                PostTripCompleted = false,
                PreTripDriverSignatureName = null,
                PreTripSignedAtUtc = null,
                PostTripDriverSignatureName = null,
                PostTripSignedAtUtc = null,
                CarrierAckName = null,
                CarrierAckAtUtc = null,
                Remarks = string.Empty
            };

            // Fill lists with sane defaults without assuming property/ctor names on InspectionChecklistItem.
            // We use reflection to set a label property if one exists (Text/Label/Name/Title).
            log.TractorPreTrip = DefaultTractorPreTrip();
            log.TrailerPreTrip = DefaultTrailerPreTrip();
            log.TractorPostTrip = DefaultTractorPostTrip();
            log.TrailerPostTrip = DefaultTrailerPostTrip();

            // Keep legacy lists in sync (combined)
            log.PreTrip = new List<InspectionChecklistItem>();
            log.PreTrip.AddRange(CloneList(log.TractorPreTrip));
            log.PreTrip.AddRange(CloneList(log.TrailerPreTrip));

            log.PostTrip = new List<InspectionChecklistItem>();
            log.PostTrip.AddRange(CloneList(log.TractorPostTrip));
            log.PostTrip.AddRange(CloneList(log.TrailerPostTrip));

            return log;
        }

        // Convenience overloads / aliases used by older ViewModel patches
        public static InspectionLog CreateDefault(DateOnly day, string? loadId = null)
        {
            var log = CreateDefault();
            log.LocalDay = day;
            log.LoadId = loadId;

            // Add prefilled identity rows to the checklists (Motive-style: these show up at the top).
            PrefillIdentityItems(log);

            return log;
        }

        public static InspectionLog CreateDefault(DateTimeOffset localDay, string? loadId = null)
        {
            return CreateDefault(DateOnly.FromDateTime(localDay.LocalDateTime), loadId);
        }

        // Aliases for UI bindings that expect tractor/trailer separated lists
        public List<InspectionChecklistItem> PreTripTractorChecklist => TractorPreTrip;
        public List<InspectionChecklistItem> PreTripTrailerChecklist => TrailerPreTrip;
        public List<InspectionChecklistItem> PostTripTractorChecklist => TractorPostTrip;
        public List<InspectionChecklistItem> PostTripTrailerChecklist => TrailerPostTrip;

        
public static List<InspectionChecklistItem> DefaultTractorPreTrip() => BuildItems(
    // FMCSA-style walkaround (common carrier inspection items)
// Required parts & accessories (49 CFR 392.7)
    ("Required (49 CFR 392.7)", "Service brakes (incl. trailer connections)"),
    ("Required (49 CFR 392.7)", "Parking brake"),
    ("Required (49 CFR 392.7)", "Steering mechanism"),
    ("Required (49 CFR 392.7)", "Lighting devices / reflectors"),
    ("Required (49 CFR 392.7)", "Tires"),
    ("Required (49 CFR 392.7)", "Horn"),
    ("Required (49 CFR 392.7)", "Windshield wipers"),
    ("Required (49 CFR 392.7)", "Rear-vision mirrors"),
    ("Required (49 CFR 392.7)", "Coupling devices"),
    ("Required (49 CFR 392.7)", "Wheels and rims"),
    ("Required (49 CFR 392.7)", "Emergency equipment (extinguisher, triangles, spare fuses)"),

    ("Driver / cab", "Seat belt"),
    ("Driver / cab", "Steering wheel (play)"),
    ("Driver / cab", "Horn"),
    ("Driver / cab", "Windshield / glass (cracks, visibility)"),
    ("Driver / cab", "Wipers / washers"),
    ("Driver / cab", "Mirrors"),
    ("Driver / cab", "Gauges / warning lights"),
    ("Driver / cab", "Parking brake (hold test)"),
    ("Driver / cab", "Service brake (feel / response)"),

    ("Engine / under hood", "Oil level / leaks"),
    ("Engine / under hood", "Coolant level / leaks"),
    ("Engine / under hood", "Power steering fluid / leaks"),
    ("Engine / under hood", "Belts / hoses (condition)"),
    ("Engine / under hood", "Battery / cables (secure, corrosion)"),

    ("Exterior walkaround", "Headlights (low/high)"),
    ("Exterior walkaround", "Turn signals / hazards"),
    ("Exterior walkaround", "Brake lights"),
    ("Exterior walkaround", "Marker / clearance lights"),
    ("Exterior walkaround", "Reflectors / conspicuity tape"),

    ("Air / brakes", "Air lines / glad hands (tractor side)"),
    ("Air / brakes", "Air pressure build / leaks"),
    ("Air / brakes", "Brake chambers / hoses (visible)"),

    ("Tires / wheels", "Steer tires (tread, damage, inflation)"),
    ("Tires / wheels", "Drive tires (tread, damage, inflation)"),
    ("Tires / wheels", "Wheels / rims (cracks, damage)"),
    ("Tires / wheels", "Lug nuts / wheel seals (missing, leaks)"),

    ("Coupling", "Fifth wheel (mounted, secure)"),
    ("Coupling", "Kingpin / locking jaws (engaged)"),
    ("Coupling", "Safety latch / release handle"),
    ("Coupling", "Frame / platform (no cracks)"),

    ("Safety", "Fire extinguisher (charged, mounted)"),
    ("Safety", "Reflective triangles"),
    ("Safety", "Spare fuses (if applicable)"),

    ("General", "No fluid leaks under tractor"),
    ("General", "Registration / permits (if applicable)")
);

private static List<InspectionChecklistItem> DefaultTrailerPreTrip() => BuildItems(
    ("Coupling", "Trailer kingpin (no damage)"),
    ("Coupling", "Glad hands / air lines (connected, seals ok)"),
    ("Coupling", "Electrical cord (connected, not chafed)"),
    ("Coupling", "Coupling device secure (no gaps)"),

    ("Landing gear", "Landing gear (up, handle secure)"),
    ("Landing gear", "Cross members / frame (damage)"),

    ("Brakes", "Brake hoses / lines (visible, no leaks)"),
    ("Brakes", "Brake chambers (visible)"),

    ("Lights / reflectors", "Tail lights"),
    ("Lights / reflectors", "Turn signals / hazards"),
    ("Lights / reflectors", "Brake lights"),
    ("Lights / reflectors", "Marker / clearance lights"),
    ("Lights / reflectors", "Reflectors / conspicuity tape"),

    ("Tires / wheels", "Trailer tires (tread, damage, inflation)"),
    ("Tires / wheels", "Wheels / rims (cracks, damage)"),
    ("Tires / wheels", "Lug nuts / wheel seals (missing, leaks)"),

    ("Doors / body", "Doors (secure, hinges/latches)"),
    ("Doors / body", "Door seal (if applicable)"),
    ("Doors / body", "Body / roof (damage, leaks)"),

    ("Load / securement", "Load securement (straps/chains, bars, airbags)"),
    ("Load / securement", "Cargo condition / shift"),
    ("Load / securement", "Placards / hazmat (if required)")
);

private static List<InspectionChecklistItem> DefaultTractorPostTrip() => BuildItems(
    ("Post-trip", "Note defects / repairs needed"),
    ("Post-trip", "Check for new leaks"),
    ("Post-trip", "Secure tractor (parked, brakes set, doors locked)")
);

private static List<InspectionChecklistItem> DefaultTrailerPostTrip() => BuildItems(
    ("Post-trip", "Note defects / repairs needed"),
    ("Post-trip", "Check load securement / damages"),
    ("Post-trip", "Secure trailer (doors, landing gear, seals)")
);

private static List<InspectionChecklistItem> BuildItems(params (string Category, string Name)[] items)
{
    var list = new List<InspectionChecklistItem>();
    for (var i = 0; i < items.Length; i++)
    {
        var (category, name) = items[i];
        var key = $"chk_{i:00}";
        var item = new InspectionChecklistItem(category, name, key)
        {
            IsOk = false,
            IsDefect = false,
            Note = string.Empty
        };
        list.Add(item);
    }
    return list;
}

private static List<InspectionChecklistItem> Build(params string[] labels)
        {
            var list = new List<InspectionChecklistItem>();
            for (var i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                var key = "chk_" + i.ToString("00");
                // Use IsOk as the checkbox state (unchecked by default).
                var item = new InspectionChecklistItem("Checklist", label, key)
                {
                    IsOk = false,
                    IsDefect = false,
                    Note = string.Empty
                };
                list.Add(item);
            }
            return list;
        }

        private static List<InspectionChecklistItem> CloneList(List<InspectionChecklistItem> src)
        {
            var list = new List<InspectionChecklistItem>(src.Count);
            foreach (var s in src)
            {
                var item = new InspectionChecklistItem(s.Category, s.Name, s.Key)
                {
                    IsOk = s.IsOk,
                    IsDefect = s.IsDefect,
                    Note = s.Note ?? string.Empty
                };
                list.Add(item);
            }
            return list;
        }
    }
}
