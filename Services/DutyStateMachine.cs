using System;
using OverWatchELD.Models;
using OverWatchELD.ViewModels;
using DutyStatus = OverWatchELD.Models.DutyStatus;

namespace OverWatchELD.Services
{
    public sealed class DutyStateMachine
    {
        private DutyStatus _current = DutyStatus.OffDuty;

        public DateTimeOffset CurrentStartedUtc { get; private set; } = EldClock.UtcNow;

        public DutyStatus Current
        {
            get => _current;
            set => SetInternal(value, "direct", force: false);
        }

        public event Action<DutyStatus>? DutyChanged;

        public bool CanSet(DutyStatus next)
        {
            if (next == DutyStatus.Driving)
                return true;

            if (next == _current)
                return false;

            if (_current == DutyStatus.Driving &&
                (next == DutyStatus.PersonalConveyance ||
                 next == DutyStatus.YardMove))
                return false;

            return true;
        }

        public void ForceSet(DutyStatus next)
        {
            SetInternal(next, "force", force: true);
        }

        public bool TrySet(DutyStatus next)
        {
            if (!CanSet(next))
                return false;

            SetInternal(next, "auto", force: false);
            return true;
        }

        private void SetInternal(DutyStatus next, string source, bool force)
        {
            if (_current == next && !force)
                return;

            var previous = _current;
            var nowUtc = EldClock.UtcNow;
            CurrentStartedUtc = nowUtc;

            try
            {
                DatabaseService.CloseOpenDutyEvent(nowUtc);

                DatabaseService.InsertDutyEvent(new DutyEvent
                {
                    Status = next,
                    StartUtc = nowUtc,
                    EndUtc = null,
                    Notes = "",
                    Source = source,
                    LocationText = "",
                    Lat = null,
                    Lon = null,
                    IsEdited = false,
                    EditedAtUtc = null,
                    EditReason = ""
                });
            }
            catch { }

            _current = next;

            try { DutyChanged?.Invoke(_current); } catch { }
            try { DashboardClocksLiveViewModel.Shared.RefreshNow(); } catch { }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[DUTY] {previous} -> {next} ({source})");
            }
            catch { }
        }

        public void Perform34HourReset()
        {
            try
            {
                TrySetIntField("_cycleUsedMinutes", 0);
                TrySetIntField("_shiftUsedMinutes", 0);
                TrySetIntField("_driveUsedMinutes", 0);

                TrySetNullableDateTimeField("_shiftStartTime", null);
                TrySetNullableDateTimeField("_lastBreakTime", null);

                TryClearListField("_cycleHistory");
                TryClearListField("_cycleDays");
                TryClearListField("_rollingCycleDays");

                InvokeIfExists("RecalculateFromLogs");
                InvokeIfExists("RebuildFromLogs");
                InvokeIfExists("RebuildFromDutyEvents");
                InvokeIfExists("ComputeClocks");
                InvokeIfExists("RecomputeClocks");
                InvokeIfExists("RefreshClocks");

                InvokeIfExists("OnStateChanged");
                InvokeIfExists("RaiseStateChanged");
                InvokeIfExists("NotifyAll");
                InvokeIfExists("RaiseAll");
            }
            catch { }
        }

        private void TrySetIntField(string fieldName, int value)
        {
            try
            {
                var f = GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);

                if (f != null && f.FieldType == typeof(int))
                    f.SetValue(this, value);
            }
            catch { }
        }

        private void TrySetNullableDateTimeField(string fieldName, DateTime? value)
        {
            try
            {
                var f = GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);

                if (f == null) return;

                if (f.FieldType == typeof(DateTime?))
                    f.SetValue(this, value);
                else if (f.FieldType == typeof(DateTime))
                    f.SetValue(this, value ?? default);
            }
            catch { }
        }

        private void TryClearListField(string fieldName)
        {
            try
            {
                var f = GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);

                var v = f?.GetValue(this);
                if (v is System.Collections.IList list)
                    list.Clear();
            }
            catch { }
        }

        private void InvokeIfExists(string methodName)
        {
            try
            {
                var m = GetType().GetMethod(methodName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);

                if (m != null && m.GetParameters().Length == 0)
                    m.Invoke(this, null);
            }
            catch { }
        }
    }
}
