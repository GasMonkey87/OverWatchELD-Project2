using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ATS.Dispatcher.Services
{
    public static class ModDefinitionScanner
    {
        public static List<string> CargoNames { get; private set; } = new();
        public static List<string> TrailerNames { get; private set; } = new();

        // ✅ Generated jobs from scanned cargo defs
        public static List<GeneratedDispatchJob> GeneratedJobs { get; private set; } = new();

        public static void ScanMods()
        {
            CargoNames.Clear();
            TrailerNames.Clear();
            GeneratedJobs.Clear();

            var modPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "American Truck Simulator",
                "mod");

            if (!Directory.Exists(modPath))
                return;

            var seenCargo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenTrailer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenJobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.GetFiles(modPath)
                         .Where(f => f.EndsWith(".scs", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var zip = ZipFile.OpenRead(file);

                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName.StartsWith("def/cargo/", StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.EndsWith(".sii", StringComparison.OrdinalIgnoreCase))
                        {
                            using var reader = new StreamReader(entry.Open());
                            var text = reader.ReadToEnd();

                            var m = Regex.Match(
                                text,
                                @"cargo_data:\s*([a-z0-9_\.]+)",
                                RegexOptions.IgnoreCase);

                            if (m.Success)
                            {
                                var cargoId = m.Groups[1].Value.Trim();

                                if (!string.IsNullOrWhiteSpace(cargoId) && seenCargo.Add(cargoId))
                                {
                                    CargoNames.Add(cargoId);

                                    // ✅ Generate dispatch job from cargo
                                    var job = BuildJobFromCargo(file, cargoId);
                                    if (job != null && seenJobs.Add(job.Id))
                                    {
                                        GeneratedJobs.Add(job);
                                        TryPushToDispatchService(job);
                                    }
                                }
                            }
                        }
                        else if (entry.FullName.StartsWith("def/vehicle/trailer/", StringComparison.OrdinalIgnoreCase) &&
                                 entry.FullName.EndsWith(".sii", StringComparison.OrdinalIgnoreCase))
                        {
                            using var reader = new StreamReader(entry.Open());
                            var text = reader.ReadToEnd();

                            var m = Regex.Match(
                                text,
                                @"trailer_def:\s*([a-z0-9_\.]+)",
                                RegexOptions.IgnoreCase);

                            if (m.Success)
                            {
                                var trailerId = m.Groups[1].Value.Trim();

                                if (!string.IsNullOrWhiteSpace(trailerId) && seenTrailer.Add(trailerId))
                                    TrailerNames.Add(trailerId);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading {file}: {ex.Message}");
                }
            }

            CargoNames.Sort(StringComparer.OrdinalIgnoreCase);
            TrailerNames.Sort(StringComparer.OrdinalIgnoreCase);

            GeneratedJobs = GeneratedJobs
                .OrderBy(x => x.Cargo, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static GeneratedDispatchJob? BuildJobFromCargo(string modFilePath, string cargoId)
        {
            cargoId = (cargoId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cargoId))
                return null;

            var friendlyCargo = ToFriendlyName(cargoId);
            var modName = Path.GetFileName(modFilePath);

            // Safe defaults until city/company routing is added
            var origin = "ATS Dispatch Pool";
            var destination = "Assigned Route";
            var distance = 0;

            var stableId = $"mod-{MakeSafeId(Path.GetFileNameWithoutExtension(modName))}-{MakeSafeId(cargoId)}";

            return new GeneratedDispatchJob
            {
                Id = stableId,
                Cargo = friendlyCargo,
                Origin = origin,
                Destination = destination,
                Distance = distance,
                SourceMod = modName,
                CargoId = cargoId
            };
        }

        private static void TryPushToDispatchService(GeneratedDispatchJob job)
        {
            try
            {
                // Looks for OverWatchELD.Services.DispatchService / OverWatchELD.Services.Job
                // without creating a hard compile dependency.
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();

                Type? dispatchServiceType = null;
                Type? jobType = null;

                foreach (var asm in assemblies)
                {
                    dispatchServiceType ??= asm.GetType("OverWatchELD.Services.DispatchService", throwOnError: false);
                    jobType ??= asm.GetType("OverWatchELD.Services.Job", throwOnError: false);

                    if (dispatchServiceType != null && jobType != null)
                        break;
                }

                if (dispatchServiceType == null || jobType == null)
                    return;

                var addJobMethod = dispatchServiceType.GetMethod(
                    "AddJob",
                    BindingFlags.Public | BindingFlags.Static);

                if (addJobMethod == null)
                    return;

                var jobObj = Activator.CreateInstance(jobType);
                if (jobObj == null)
                    return;

                SetProp(jobType, jobObj, "Id", job.Id);
                SetProp(jobType, jobObj, "Cargo", job.Cargo);
                SetProp(jobType, jobObj, "Origin", job.Origin);
                SetProp(jobType, jobObj, "Destination", job.Destination);
                SetProp(jobType, jobObj, "Distance", job.Distance);

                addJobMethod.Invoke(null, new[] { jobObj });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Dispatch push failed: {ex.Message}");
            }
        }

        private static void SetProp(Type t, object obj, string propName, object? value)
        {
            try
            {
                var p = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite) return;
                p.SetValue(obj, value);
            }
            catch { }
        }

        private static string ToFriendlyName(string raw)
        {
            raw = (raw ?? "").Trim();

            if (string.IsNullOrWhiteSpace(raw))
                return "Custom Load";

            var last = raw.Split('.').LastOrDefault() ?? raw;
            last = last.Replace('_', ' ').Replace('-', ' ').Trim();

            if (string.IsNullOrWhiteSpace(last))
                return "Custom Load";

            return Regex.Replace(last, @"\s+", " ")
                        .Trim();
        }

        private static string MakeSafeId(string value)
        {
            value = (value ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
                return "item";

            value = Regex.Replace(value, @"[^a-z0-9]+", "-").Trim('-');
            return string.IsNullOrWhiteSpace(value) ? "item" : value;
        }

        public sealed class GeneratedDispatchJob
        {
            public string Id { get; set; } = "";
            public string CargoId { get; set; } = "";
            public string Cargo { get; set; } = "";
            public string Origin { get; set; } = "";
            public string Destination { get; set; } = "";
            public int Distance { get; set; }
            public string SourceMod { get; set; } = "";
        }
    }
}