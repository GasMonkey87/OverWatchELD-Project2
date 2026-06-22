// ViewModels/DashboardViewModel.Tick.cs
// ✅ FULL COPY/REPLACE
// Keeps Tick() method used by DashboardView.xaml.cs.
// Fixes CS0176 by calling DashboardSnapshotProvider.BuildSnapshot() statically.

using System;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public partial class DashboardViewModel
    {
        // Called from DashboardView.xaml.cs timer/dispatcher
        public void Tick()
        {
            try
            {
                // Very lightweight snapshot update
                Snapshot = DashboardSnapshotProvider.BuildSnapshot();
            }
            catch
            {
                // ignore
            }
        }
    }
}