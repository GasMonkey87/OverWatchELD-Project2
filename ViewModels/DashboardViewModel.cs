// ViewModels/DashboardViewModel.cs

using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OverWatchELD.Models;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public partial class DashboardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly DispatcherTimer _clockRefreshTimer;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string F(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;

            var totalHours = (int)Math.Floor(t.TotalHours);
            return $"{totalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        }

        private static bool IsWorkStatus(DutyStatus status)
        {
            return status == DutyStatus.Driving ||
                   status == DutyStatus.OnDuty ||
                   status == DutyStatus.YardMove;
        }

        private static (TimeSpan drive, TimeSpan shift, TimeSpan brk, TimeSpan cycle, string duty) GetLiveClockValues(DashboardSnapshotProvider.DashboardSnapshot snapshot)
        {
            try
            {
                var machine = (Application.Current as App)?.DutyMachine;
                if (machine == null)
                    return (snapshot.DriveRemaining, snapshot.ShiftRemaining, snapshot.BreakRemaining, snapshot.CycleRemaining, snapshot.DutyStatusLabel ?? "Unknown");

                var elapsed = EldClock.UtcNow - machine.CurrentStartedUtc;
                if (elapsed < TimeSpan.Zero)
                    elapsed = TimeSpan.Zero;

                var status = machine.Current;
                var driveUsed = status == DutyStatus.Driving ? elapsed : TimeSpan.Zero;
                var workUsed = IsWorkStatus(status) ? elapsed : TimeSpan.Zero;

                return (
                    TimeSpan.FromHours(11) - driveUsed,
                    TimeSpan.FromHours(14) - workUsed,
                    status == DutyStatus.Driving ? TimeSpan.FromHours(8) - elapsed : TimeSpan.FromHours(8),
                    TimeSpan.FromHours(70) - workUsed,
                    status.ToString()
                );
            }
            catch
            {
                return (snapshot.DriveRemaining, snapshot.ShiftRemaining, snapshot.BreakRemaining, snapshot.CycleRemaining, snapshot.DutyStatusLabel ?? "Unknown");
            }
        }

        private DashboardSnapshotProvider.DashboardSnapshot _snapshot = new DashboardSnapshotProvider.DashboardSnapshot();

        public DashboardSnapshotProvider.DashboardSnapshot Snapshot
        {
            get => _snapshot;
            private set
            {
                _snapshot = value ?? new DashboardSnapshotProvider.DashboardSnapshot();
                NotifyAll();
            }
        }

        public string DriveTime => F(GetLiveClockValues(Snapshot).drive);
        public string ShiftTime => F(GetLiveClockValues(Snapshot).shift);
        public string BreakTime => F(GetLiveClockValues(Snapshot).brk);
        public string CycleTime => F(GetLiveClockValues(Snapshot).cycle);

        public string DutyStatusLabel => GetLiveClockValues(Snapshot).duty;
        public bool ShouldPulse => Snapshot.ShouldPulse;

        public int UnsignedLogsCount => Snapshot.UnsignedLogsCount;
        public bool HasUnsignedLogs => UnsignedLogsCount > 0;

        private string _truckConnectionStatus = "Disconnected";
        public string TruckConnectionStatus
        {
            get => _truckConnectionStatus;
            set { _truckConnectionStatus = value; OnPropertyChanged(); }
        }

        private string _truckNumberText = "Truck #: N/A";
        public string TruckNumberText
        {
            get => _truckNumberText;
            set { _truckNumberText = value; OnPropertyChanged(); }
        }

        private string _truckNameText = "Truck: Unknown";
        public string TruckNameText
        {
            get => _truckNameText;
            set { _truckNameText = value; OnPropertyChanged(); }
        }

        public DashboardViewModel()
        {
            try { Snapshot = DashboardSnapshotProvider.BuildSnapshot(); }
            catch { Snapshot = CreateFreshResetSnapshot(); }

            _clockRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockRefreshTimer.Tick += (_, _) => RefreshDashboardClockSnapshot();
            _clockRefreshTimer.Start();
        }

        private void RefreshDashboardClockSnapshot()
        {
            try
            {
                Snapshot = DashboardSnapshotProvider.BuildSnapshot();
            }
            catch
            {
                OnPropertyChanged(nameof(DriveTime));
                OnPropertyChanged(nameof(ShiftTime));
                OnPropertyChanged(nameof(BreakTime));
                OnPropertyChanged(nameof(CycleTime));
                OnPropertyChanged(nameof(DutyStatusLabel));
            }
        }

        public async Task RefreshAsync()
        {
            await Task.Yield();

            try { RefreshDashboardClockSnapshot(); }
            catch { }
        }

        public void RefreshUnsignedLogsCount()
        {
            try
            {
                Snapshot = DashboardSnapshotProvider.BuildSnapshot();
            }
            catch
            {
                OnPropertyChanged(nameof(UnsignedLogsCount));
                OnPropertyChanged(nameof(HasUnsignedLogs));
            }
        }

        public void ResetClocks() => ResetAllClocks();
        public void ResetHosClocks() => ResetAllClocks();
        public void ResetTimers() => ResetAllClocks();
        public void Reset() => ResetAllClocks();

        public void ResetAllClocks()
        {
            try
            {
                DatabaseService.InsertHosClockResetEvent();
                Snapshot = DashboardSnapshotProvider.BuildSnapshot();
            }
            catch
            {
                try { Snapshot = DashboardSnapshotProvider.BuildSnapshot(); } catch { }
            }
        }

        private static void TryCallResetMethod(string typeName)
        {
            try
            {
                var type = Type.GetType(typeName);
                if (type == null)
                    return;

                foreach (var methodName in new[]
                {
                    "ResetClocks",
                    "ResetAllClocks",
                    "ResetHosClocks",
                    "ResetTimers",
                    "Reset"
                })
                {
                    var method = type.GetMethod(
                        methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (method != null)
                    {
                        method.Invoke(null, null);
                        return;
                    }
                }
            }
            catch { }
        }

        private static DashboardSnapshotProvider.DashboardSnapshot CreateFreshResetSnapshot()
        {
            var snap = new DashboardSnapshotProvider.DashboardSnapshot();

            SetProperty(snap, "DriveRemaining", TimeSpan.FromHours(11));
            SetProperty(snap, "ShiftRemaining", TimeSpan.FromHours(14));
            SetProperty(snap, "BreakRemaining", TimeSpan.FromHours(8));
            SetProperty(snap, "CycleRemaining", TimeSpan.FromHours(70));
            SetProperty(snap, "DutyStatusLabel", "Off Duty");
            SetProperty(snap, "ShouldPulse", false);

            return snap;
        }

        private static void SetProperty(object obj, string name, object value)
        {
            try
            {
                var prop = obj.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop != null && prop.CanWrite)
                    prop.SetValue(obj, value);
            }
            catch { }
        }

        private void NotifyAll()
        {
            OnPropertyChanged(nameof(Snapshot));
            OnPropertyChanged(nameof(DriveTime));
            OnPropertyChanged(nameof(ShiftTime));
            OnPropertyChanged(nameof(BreakTime));
            OnPropertyChanged(nameof(CycleTime));
            OnPropertyChanged(nameof(DutyStatusLabel));
            OnPropertyChanged(nameof(ShouldPulse));
            OnPropertyChanged(nameof(UnsignedLogsCount));
            OnPropertyChanged(nameof(HasUnsignedLogs));
        }
    }
}
