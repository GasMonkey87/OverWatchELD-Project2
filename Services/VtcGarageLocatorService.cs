using System;
using System.Linq;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class VtcGarageLocatorService
    {
        public static VtcGarage? FindNearestOwnedGarage(
            double? mapX,
            double? mapY)
        {
            if (mapX == null || mapY == null)
                return null;

            var garages = VtcGarageStore.Load()
                .Where(x => x.IsOwned)
                .ToList();

            VtcGarage? best = null;
            double bestDist = double.MaxValue;

            foreach (var g in garages)
            {
                if (g.MapX == null || g.MapY == null)
                    continue;

                var dx = g.MapX.Value - mapX.Value;
                var dy = g.MapY.Value - mapY.Value;

                var dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = g;
                }
            }

            return best;
        }
    }
}