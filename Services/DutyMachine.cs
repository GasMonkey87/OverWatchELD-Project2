using System;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Minimal, stable duty-state service used by UI to subscribe to status updates.
    /// This exists to satisfy App.DutyMachine references and prevent build breaks.
    /// You can later wire it to your real HOS/duty logic without changing subscribers.
    /// </summary>
    public sealed class DutyMachine
    {
        // Generic events so existing method groups / lambdas can subscribe easily.
        public event EventHandler? StateChanged;
        public event EventHandler? DutyStatusChanged;
        public event EventHandler? DutyEventAdded;

        // Keep a simple current status (string avoids enum/type coupling).
        public string CurrentStatus { get; private set; } = "OFF";

        public void SetStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                status = "OFF";

            CurrentStatus = status.Trim().ToUpperInvariant();

            // Fire both, so any subscriber style still works.
            DutyStatusChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void NotifyEventAdded()
        {
            DutyEventAdded?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void NotifyStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
