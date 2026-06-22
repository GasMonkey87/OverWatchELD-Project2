using OverWatchELD.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OverWatchELD.ViewModels
{
    public sealed class DriverScoreViewModel : INotifyPropertyChanged
    {
        private readonly string _driverId;

        public string DriverName { get; }
        public ObservableCollection<DriverHistoryEntry> RecentHistory { get; } = new();

        private DriverPerformanceStore.DriverPerf _perf = new();
        public DriverPerformanceStore.DriverPerf Perf
        {
            get => _perf;
            private set
            {
                _perf = value ?? new DriverPerformanceStore.DriverPerf();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScoreDisplay));
                OnPropertyChanged(nameof(MilesDisplay));
                OnPropertyChanged(nameof(LoadsDisplay));
                OnPropertyChanged(nameof(BehaviorDisplay));
            }
        }

        public string ScoreDisplay => $"{Perf.Score:N0}";
        public string MilesDisplay => $"Today: {Perf.MilesToday:N1}   •   Week: {Perf.MilesWeek:N1}   •   Total: {Perf.MilesTotal:N1}";
        public string LoadsDisplay => $"Today: {Perf.LoadsToday}   •   Week: {Perf.LoadsWeek}   •   Total: {Perf.LoadsTotal}";
        public string BehaviorDisplay =>
            $"Hard Brakes: {Perf.HardBrakes}   •   Overspeed: {Perf.OverspeedEvents}   •   Idle: {Perf.IdleMinutes} min   •   HOS: {Perf.HosViolations}";

        public DriverScoreViewModel(string driverId, string driverName)
        {
            _driverId = (driverId ?? "").Trim();
            DriverName = string.IsNullOrWhiteSpace(driverName) ? "Driver Score" : driverName.Trim();
            Refresh();
        }

        public void Refresh()
        {
            Perf = DriverPerformanceStore.Get(_driverId);

            RecentHistory.Clear();
            foreach (var item in DriverHistoryStore.GetRecent(_driverId, 50))
                RecentHistory.Add(item);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}