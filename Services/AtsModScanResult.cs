using System;
using System.Collections.Generic;
using System.Linq;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Compatibility DTO for older code that still expects AtsModScanResult.
    /// New scanner code can fill this from AtsResolvedDefinitionCache.
    /// </summary>
    public sealed class AtsModScanResult
    {
        public DateTime GeneratedUtc { get; set; }
        public string ModFolder { get; set; } = "";
        public string ProfileName { get; set; } = "";

        public List<AtsDefinitionSource> Sources { get; set; } = new();
        public List<AtsResolvedCargoDef> Cargoes { get; set; } = new();
        public List<AtsResolvedTrailerDef> Trailers { get; set; } = new();
        public List<AtsResolvedCompanyDef> Companies { get; set; } = new();
        public List<AtsResolvedCityDef> Cities { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<AtsResolvedCargoDef> Cargos
        {
            get => Cargoes;
            set => Cargoes = value;
        }
        public static AtsModScanResult Empty(string? warning = null)
        {
            var result = new AtsModScanResult
            {
                GeneratedUtc = DateTime.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(warning))
                result.Warnings.Add(warning);

            return result;
        }

        public static AtsModScanResult FromCache(AtsResolvedDefinitionCache? cache)
        {
            if (cache == null)
                return Empty("ATS definition cache was null.");

            return new AtsModScanResult
            {
                GeneratedUtc = cache.GeneratedUtc,
                ModFolder = cache.ModFolder ?? "",
                ProfileName = cache.ProfileName ?? "",
                Sources = cache.Sources?.ToList() ?? new List<AtsDefinitionSource>(),
                Cargoes = cache.Cargoes?.ToList() ?? new List<AtsResolvedCargoDef>(),
                Trailers = cache.Trailers?.ToList() ?? new List<AtsResolvedTrailerDef>(),
                Companies = cache.Companies?.ToList() ?? new List<AtsResolvedCompanyDef>(),
                Cities = cache.Cities?.ToList() ?? new List<AtsResolvedCityDef>(),
                Warnings = cache.Warnings?.ToList() ?? new List<string>()
            };
        }
    }
}