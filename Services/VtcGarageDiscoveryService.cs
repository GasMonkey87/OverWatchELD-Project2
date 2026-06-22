using System.Collections.Generic;
using System.Linq;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class VtcGarageDiscoveryService
    {
        public static List<VtcGarage> LoadAtsGarages()
        {
            return AtsGarageCatalog.Create()
                .OrderBy(x => x.State)
                .ThenBy(x => x.CityName)
                .ToList();
        }
    }
}