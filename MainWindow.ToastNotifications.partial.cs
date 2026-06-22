using System;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace OverWatchELD
{
    public partial class MainWindow
    {
        private int _toastLastCount = -1;
        private DateTime _toastLastShownUtc = DateTime.MinValue;
        private bool _toastHooked;

        private static void OnAnyMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow mw) return;
            mw.TryHookToasts_ReflectionSafe();
        }

        private void TryHookToasts_ReflectionSafe()
        {
            if (_toastHooked) return;
            _toastHooked = true;

            try
            {
                var app = Application.Current;
                if (app == null) return;

                // Try: app.DispatchInbox (any type) -> has Inbox + Changed + MessageReceived
                var inboxObj = app.GetType().GetProperty("DispatchInbox",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(app);

                if (inboxObj == null) return;

                // Hook MessageReceived if present
                var msgReceivedEvent = inboxObj.GetType().GetEvent("MessageReceived",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (msgReceivedEvent != null)
                {
                    // event signature might be Action<DispatchMessage> or EventHandler<...>
                    // We'll attach a generic handler via reflection
                    var handler = CreateMessageReceivedHandler(inboxObj);
                    if (handler != null)
                        msgReceivedEvent.AddEventHandler(inboxObj, handler);
                }

                // Hook Changed if present
                var changedEvent = inboxObj.GetType().GetEvent("Changed",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (changedEvent != null)
                {
                    var handler = CreateChangedHandler(inboxObj);
                    if (handler != null)
                        changedEvent.AddEventHandler(inboxObj, handler);
                }
                else
                {
                    // Sometimes it's a property Action Changed {get;set;}
                    var changedProp = inboxObj.GetType().GetProperty("Changed",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var action = changedProp?.GetValue(inboxObj) as Delegate;
                    // leave it alone if not an event
                }
            }
            catch
            {
                // swallow — toasts are optional
            }
        }

        private Delegate? CreateChangedHandler(object inboxObj)
        {
            try
            {
                // Use the event handler type and build a compatible delegate
                var evt = inboxObj.GetType().GetEvent("Changed",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (evt == null) return null;

                var handlerType = evt.EventHandlerType;
                if (handlerType == null) return null;

                // Try to create delegate to method OnInboxChanged(object?, EventArgs?) OR Action()
                var mi = GetType().GetMethod(nameof(OnInboxChanged_ReflectionSafe),
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (mi == null) return null;

                return Delegate.CreateDelegate(handlerType, this, mi, throwOnBindFailure: false);
            }
            catch { return null; }
        }

        private Delegate? CreateMessageReceivedHandler(object inboxObj)
        {
            try
            {
                var evt = inboxObj.GetType().GetEvent("MessageReceived",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (evt == null) return null;

                var handlerType = evt.EventHandlerType;
                if (handlerType == null) return null;

                var mi = GetType().GetMethod(nameof(OnInboxMessageReceived_ReflectionSafe),
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (mi == null) return null;

                return Delegate.CreateDelegate(handlerType, this, mi, throwOnBindFailure: false);
            }
            catch { return null; }
        }

        // Handles Changed event (any signature, extra args ignored by delegate bind if compatible)
        private void OnInboxChanged_ReflectionSafe(object? a = null, object? b = null)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var inboxObj = app.GetType().GetProperty("DispatchInbox",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(app);

                if (inboxObj == null) return;

                var inboxProp = inboxObj.GetType().GetProperty("Inbox",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var inboxList = inboxProp?.GetValue(inboxObj) as System.Collections.IEnumerable;
                if (inboxList == null) return;

                // count + last
                int count = 0;
                object? last = null;
                foreach (var item in inboxList) { count++; last = item; }

                if (_toastLastCount < 0) _toastLastCount = count;
                if (count <= _toastLastCount) return;

                _toastLastCount = count;

                if (last != null)
                    OnInboxMessageReceived_ReflectionSafe(last);
            }
            catch { }
        }

        // Handles MessageReceived event (signature varies)
        private void OnInboxMessageReceived_ReflectionSafe(object? msg, object? _unused = null)
        {
            try
            {
                if (msg == null) return;

                // Try extract Title/Body/Sender/Preview using common names
                string title =
                    GetString(msg, "Title") ??
                    GetString(msg, "Subject") ??
                    "New message";

                string body =
                    GetString(msg, "Body") ??
                    GetString(msg, "Text") ??
                    GetString(msg, "Preview") ??
                    "";

                // Throttle spam
                var now = DateTime.UtcNow;
                if ((now - _toastLastShownUtc) < TimeSpan.FromSeconds(2)) return;
                _toastLastShownUtc = now;

                // If you already have a toast system, call it here.
                // For now: no-op / optional MessageBox for debug:
                // MessageBox.Show(body, title);

            }
            catch { }
        }

        private static string? GetString(object obj, string prop)
        {
            try
            {
                var pi = obj.GetType().GetProperty(prop,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return pi?.GetValue(obj)?.ToString();
            }
            catch { return null; }
        }
    }
}
