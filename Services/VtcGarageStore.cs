using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OverWatchELD.Models;
using OverWatchELD.Services.Discord;

namespace OverWatchELD.Services;

public static class VtcGarageStore
{
    private static string FilePath =>
        AppPaths.FileInConfig("vtc_garages.json");

    public static List<VtcGarage> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var seeded = AtsGarageCatalog.Create();
                Save(seeded);
                return seeded;
            }

            var garages = JsonSerializer.Deserialize<List<VtcGarage>>(
                File.ReadAllText(FilePath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            ) ?? new List<VtcGarage>();

            var changed = MergeCatalogDefaults(garages);

            foreach (var g in garages)
            {
                g.AssignedTruckNumbers ??= new List<string>();
                g.TruckCapacity = CapacityForSize(g.Size);
                g.ApplyEconomyDefaults();
            }

            // IMPORTANT:
            // Existing users already have vtc_garages.json.
            // When new DLC states/cities are added to AtsGarageCatalog, merge and persist
            // the new unowned garages so they appear in Garage Ownership immediately.
            if (changed)
                Save(garages);

            return garages;
        }
        catch
        {
            return AtsGarageCatalog.Create();
        }
    }

    public static void Save(List<VtcGarage> garages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        foreach (var g in garages)
        {
            g.AssignedTruckNumbers ??= new List<string>();
            g.TruckCapacity = CapacityForSize(g.Size);
            g.ApplyEconomyDefaults();
        }

        File.WriteAllText(
            FilePath,
            JsonSerializer.Serialize(garages, new JsonSerializerOptions
            {
                WriteIndented = true
            }));

        try
        {
            var owned = garages.Count(g => g.IsOwned);
            DiscordNotificationPushService.PushFireAndForget(
                "Garages",
                "Garage Board Updated",
                $"VTC garage board saved with {owned:N0} owned garage(s).",
                $"Total garages tracked: {garages.Count:N0}");
        }
        catch
        {
        }
    }

    public static bool CanAssignTruck(VtcGarage garage)
    {
        garage.AssignedTruckNumbers ??= new List<string>();
        return garage.AssignedTruckNumbers.Count < garage.TruckCapacity;
    }

    public static int CapacityForSize(string? size)
    {
        return (size ?? "").Trim().ToLowerInvariant() switch
        {
            "large" => 7,
            "medium" or "med" => 5,
            _ => 3
        };
    }

    private static bool MergeCatalogDefaults(List<VtcGarage> garages)
    {
        var changed = false;
        var catalog = AtsGarageCatalog.Create();

        foreach (var cat in catalog)
        {
            var existing = garages.Find(g =>
                Same(g.Id, cat.Id) ||
                Same(g.CityName, cat.CityName) && Same(g.State, cat.State));

            if (existing == null)
            {
                garages.Add(CloneCatalogGarage(cat));
                changed = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(existing.Id))
            {
                existing.Id = cat.Id;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(existing.CityToken))
            {
                existing.CityToken = cat.CityToken;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(existing.CityName))
            {
                existing.CityName = cat.CityName;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(existing.State))
            {
                existing.State = cat.State;
                changed = true;
            }

            if (existing.MapX == null)
            {
                existing.MapX = cat.MapX;
                changed = true;
            }

            if (existing.MapY == null)
            {
                existing.MapY = cat.MapY;
                changed = true;
            }

            existing.AssignedTruckNumbers ??= new List<string>();
            existing.TruckCapacity = CapacityForSize(existing.Size);
            existing.ApplyEconomyDefaults();
        }

        return changed;
    }

    private static VtcGarage CloneCatalogGarage(VtcGarage source)
    {
        var garage = new VtcGarage
        {
            Id = source.Id,
            CityToken = source.CityToken,
            CityName = source.CityName,
            State = source.State,
            Size = string.IsNullOrWhiteSpace(source.Size) ? "Small" : source.Size,
            TruckCapacity = CapacityForSize(source.Size),
            IsOwned = false,
            IsHomeGarage = false,
            HasFuelStation = false,
            MapX = source.MapX,
            MapY = source.MapY,
            AssignedTruckNumbers = new List<string>()
        };

        garage.ApplyEconomyDefaults();

        return garage;
    }

    private static bool Same(string? a, string? b)
    {
        return !string.IsNullOrWhiteSpace(a) &&
               !string.IsNullOrWhiteSpace(b) &&
               string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static List<VtcGarage> SeedDefaultGarages()
    {
        return AtsGarageCatalog.Create();
    }

    private static VtcGarage Garage(string token, string city, string state, string size)
    {
        var garage = new VtcGarage
        {
            Id = token,
            CityToken = token,
            CityName = city,
            State = state,
            Size = size,
            TruckCapacity = CapacityForSize(size),
            IsOwned = false,
            AssignedTruckNumbers = new List<string>()
        };

        garage.ApplyEconomyDefaults();

        return garage;
    }
}
