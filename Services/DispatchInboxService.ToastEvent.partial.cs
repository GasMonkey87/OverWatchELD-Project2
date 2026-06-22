// Services/DispatchInboxService.ToastEvent.partial.cs
// ✅ FULL COPY/REPLACE
// Fixes CS0116 by ensuring everything is inside the partial class.
// Purpose: optional Windows toast/notification helper when dispatch arrives.
// Safe: if tray/toast not available, it silently no-ops.

using System;

namespace OverWatchELD.Services
{
    public sealed partial class DispatchInboxService
    {
        // Call this from anywhere you want to "toast" on new dispatch.
        // It’s intentionally best-effort (never throws).
        private void ToastDispatchReceived(string title, string body)
        {
            try
            {
                // If you have a real toast system elsewhere, wire it here.
                // Keeping this as a no-op prevents build breaks across variants.
            }
            catch { }
        }
    }
}
