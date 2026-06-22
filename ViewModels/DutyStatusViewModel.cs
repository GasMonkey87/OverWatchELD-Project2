using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using OverWatchELD.Models;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public sealed class DutyStatusViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private DutyStatus _currentStatus = DutyStatus.OffDuty;
        private readonly DutyStateMachine? _machine;

        public DutyStatus CurrentStatus
        {
            get => _currentStatus;
            private set
            {
                if (_currentStatus == value) return;

                _currentStatus = value;
                OnPropertyChanged();
                RaiseAllStatusFlags();

                try { DashboardClocksLiveViewModel.Shared.RefreshNow(); } catch { }
            }
        }

        public bool IsOffDuty
        {
            get => CurrentStatus == DutyStatus.OffDuty;
            set { if (value) SetStatus(DutyStatus.OffDuty); }
        }

        public bool IsSleeperBerth
        {
            get => CurrentStatus == DutyStatus.Sleeper;
            set { if (value) SetStatus(DutyStatus.Sleeper); }
        }

        public bool IsDriving
        {
            get => CurrentStatus == DutyStatus.Driving;
            set { if (value) SetStatus(DutyStatus.Driving); }
        }

        public bool IsOnDuty
        {
            get => CurrentStatus == DutyStatus.OnDuty;
            set { if (value) SetStatus(DutyStatus.OnDuty); }
        }

        public bool IsPersonalConveyance
        {
            get => CurrentStatus == DutyStatus.PersonalConveyance;
            set { if (value) SetStatus(DutyStatus.PersonalConveyance); }
        }

        public bool IsYardMove
        {
            get => CurrentStatus == DutyStatus.YardMove;
            set { if (value) SetStatus(DutyStatus.YardMove); }
        }

        public DutyStatusViewModel()
        {
            _machine = ResolveDutyMachine();

            if (_machine != null)
            {
                _machine.DutyChanged += OnMachineDutyChanged;
            }

            LoadInitialStatus();
            RaiseAllStatusFlags();
        }

        private static DutyStateMachine? ResolveDutyMachine()
        {
            try
            {
                return (Application.Current as App)?.DutyMachine;
            }
            catch
            {
                return null;
            }
        }

        private void OnMachineDutyChanged(DutyStatus status)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;

                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => CurrentStatus = status));
                    return;
                }

                CurrentStatus = status;
            }
            catch { }
        }

        private void LoadInitialStatus()
        {
            try
            {
                var nowUtc = EldClock.UtcNow;
                var events = DatabaseService.GetDutyEvents(nowUtc.AddDays(-14), nowUtc.AddMinutes(1));
                var last = events?.OrderBy(e => e.StartUtc).LastOrDefault();

                var status = last?.Status ?? DutyStatus.OffDuty;

                if (_machine != null)
                    _machine.Current = status;

                CurrentStatus = _machine?.Current ?? status;
            }
            catch
            {
                CurrentStatus = DutyStatus.OffDuty;
            }
        }

        public void SetStatus(DutyStatus newStatus)
        {
            try
            {
                if (_machine != null)
                {
                    _machine.TrySet(newStatus);
                    CurrentStatus = _machine.Current;
                    return;
                }

                ELDStateService.SetCurrentStatus(newStatus);
                CurrentStatus = ELDStateService.CurrentStatus;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Duty change failed:\n{ex.GetBaseException().Message}",
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private void RaiseAllStatusFlags()
        {
            OnPropertyChanged(nameof(IsOffDuty));
            OnPropertyChanged(nameof(IsSleeperBerth));
            OnPropertyChanged(nameof(IsDriving));
            OnPropertyChanged(nameof(IsOnDuty));
            OnPropertyChanged(nameof(IsPersonalConveyance));
            OnPropertyChanged(nameof(IsYardMove));
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}