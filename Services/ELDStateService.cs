using OverWatchELD.ViewModels;
using System;
using System.Linq;
using System.Windows;
using DutyStatus = OverWatchELD.Models.DutyStatus;

namespace OverWatchELD.Services
{
    public class ELDStateService
    {
        private static DutyStateMachine? ResolveMachine()
        {
            try
            {
                var app = Application.Current as App;
                if (app?.DutyMachine is DutyStateMachine machine)
                    return machine;
            }
            catch { }

            return null;
        }

        public static DutyStatus CurrentStatus
        {
            get
            {
                try
                {
                    var nowUtc = EldClock.UtcNow;
                    var events = DatabaseService.GetDutyEvents(nowUtc.AddDays(-14), nowUtc.AddMinutes(1));
                    var last = events?.OrderBy(e => e.StartUtc).LastOrDefault();
                    if (last != null) return last.Status;
                }
                catch { }

                var machine = ResolveMachine();
                return machine?.Current ?? DutyStatus.OffDuty;
            }
        }

        public static DateTimeOffset CurrentStatusStartUtc { get; private set; } = EldClock.UtcNow;

        public static event Action<DutyStatus>? DutyChanged;

        public static void SetCurrentStatus(DutyStatus status, string comment = "")
        {
            if (status == CurrentStatus) return;

            var machine = ResolveMachine();
            var changed = false;

            if (machine != null)
            {
                try { changed = machine.TrySet(status); } catch { changed = false; }
            }

            if (!changed)
            {
                try
                {
                    DatabaseService.CloseOpenDutyEvent(EldClock.UtcNow);
                    DatabaseService.InsertDutyEvent(new OverWatchELD.Models.DutyEvent
                    {
                        Status = status,
                        StartUtc = EldClock.UtcNow,
                        EndUtc = null,
                        Notes = comment ?? string.Empty,
                        Source = "manual",
                        LocationText = string.Empty,
                        IsEdited = false
                    });
                    changed = true;
                }
                catch { }
            }

            if (changed)
            {
                CurrentStatusStartUtc = EldClock.UtcNow;
                DutyChanged?.Invoke(status);
                try { DashboardClocksLiveViewModel.Shared.RefreshNow(); } catch { }
            }
        }

        public static string CurrentStatusTextShort() =>
            CurrentStatus switch
            {
                DutyStatus.OffDuty => "OFF",
                DutyStatus.Sleeper => "SB",
                DutyStatus.Driving => "D",
                _ => "ON"
            };
    }
}