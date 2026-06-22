using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public sealed class DriverPerformanceViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly DriverPerformanceService _svc = DriverPerformanceService.Shared;

        public DriverPerformanceViewModel()
        {
            _svc.MetricsUpdated += () =>
            {
                // Ensure UI-thread updates
                try
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new System.Action(RefreshFromService));
                }
                catch { }
            };

            RefreshFromService();
        }

        private int _hardBrakes;
        public int HardBrakes { get => _hardBrakes; set { if (_hardBrakes == value) return; _hardBrakes = value; OnPropertyChanged(); } }

        private int _overspeedEvents;
        public int OverspeedEvents { get => _overspeedEvents; set { if (_overspeedEvents == value) return; _overspeedEvents = value; OnPropertyChanged(); } }

        private int _speedingMinutes;
        public int SpeedingMinutes { get => _speedingMinutes; set { if (_speedingMinutes == value) return; _speedingMinutes = value; OnPropertyChanged(); } }

        private int _idleMinutes;
        public int IdleMinutes { get => _idleMinutes; set { if (_idleMinutes == value) return; _idleMinutes = value; OnPropertyChanged(); } }

        private double _idlePercent;
        public double IdlePercent { get => _idlePercent; set { if (_idlePercent == value) return; _idlePercent = value; OnPropertyChanged(); } }

        private int _hosViolations;
        public int HosViolations { get => _hosViolations; set { if (_hosViolations == value) return; _hosViolations = value; OnPropertyChanged(); } }

        // Optional: Keep your “score” tiles too if you want
        private int _safetyScore;
        public int SafetyScore { get => _safetyScore; set { if (_safetyScore == value) return; _safetyScore = value; OnPropertyChanged(); } }

        private int _onTimePercent;
        public int OnTimePercent { get => _onTimePercent; set { if (_onTimePercent == value) return; _onTimePercent = value; OnPropertyChanged(); } }

        private int _hosCompliance;
        public int HosCompliance { get => _hosCompliance; set { if (_hosCompliance == value) return; _hosCompliance = value; OnPropertyChanged(); } }

        private void RefreshFromService()
        {
            var s = _svc.GetSnapshot();

            HardBrakes = s.HardBrakes;
            OverspeedEvents = s.OverspeedEvents;
            SpeedingMinutes = s.SpeedingMinutes;
            IdleMinutes = s.IdleMinutes;
            IdlePercent = s.IdlePercent;
            HosViolations = s.HosViolations;

            // Simple scoring example (tweak later)
            SafetyScore = Clamp(100 - (HardBrakes * 5) - (OverspeedEvents * 3), 0, 100);
            HosCompliance = Clamp(100 - (HosViolations * 20), 0, 100);

            // Placeholder until you wire dispatch/on-time logic
            OnTimePercent = 90;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
