using System;

namespace OverWatchELD.Services
{
    public sealed class SessionStateBridge
    {
        private readonly SessionPersistenceService _persist = new();
        private SessionPersistenceService.PersistedState _state;

        public SessionStateBridge()
        {
            _state = _persist.LoadOrDefault();
        }

        public SessionPersistenceService.PersistedState State => _state;

        public void SetLogin(string driverName, string vtcProvider)
        {
            _state.DriverName = driverName ?? "Driver";
            _state.VtcProvider = vtcProvider ?? "None";
            _persist.Save(_state);
        }

        public void SetDuty(string dutyCode, DateTimeOffset changeUtc)
        {
            _state.LastDutyStatus = dutyCode;
            _state.LastDutyChangeUtc = changeUtc;
            _persist.Save(_state);
        }

        public void SetLastLogDate(DateTime localDate)
        {
            _state.LastLogDateLocal = localDate.Date;
            _persist.Save(_state);
        }
    }
}
