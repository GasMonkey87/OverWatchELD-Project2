using System;
using System.Linq;
using System.Reflection;
using OverWatchELD.ViewModels;

namespace OverWatchELD.ViewModels
{
    /// <summary>
    /// Compatibility extension methods used by some UI patches (LogsView.xaml.cs).
    /// Adds LoadPrevDay / LoadNextDay / RefreshUnsigned without requiring changes
    /// to the existing LogsViewModel implementation.
    /// </summary>
    public static class LogsViewModelExtensions
    {
        public static void LoadPrevDay(this LogsViewModel vm)
        {
            if (vm == null) return;
            ShiftDayAndReload(vm, -1);
        }

        public static void LoadNextDay(this LogsViewModel vm)
        {
            if (vm == null) return;
            ShiftDayAndReload(vm, +1);
        }

        public static void RefreshUnsigned(this LogsViewModel vm)
        {
            if (vm == null) return;

            // Try to call a dedicated method if your VM already has one (different branches use different names).
            if (TryInvoke(vm, new[]
                {
                    "RefreshUnsigned",
                    "ReloadUnsigned",
                    "LoadUnsigned",
                    "RefreshUncertified",
                    "ReloadUncertified",
                    "LoadUncertified",
                    "Refresh",
                    "Reload",
                    "Load"
                }))
            {
                return;
            }

            // Fallback: reload current day if we can find a date-like property.
            ShiftDayAndReload(vm, 0);
        }

        private static void ShiftDayAndReload(object vm, int deltaDays)
        {
            try
            {
                var dateProp = FindDateProperty(vm);
                var current = GetDateFromProperty(vm, dateProp) ?? DateTime.Today;
                var next = current.AddDays(deltaDays);

                // Set date back if we found a writable property
                if (dateProp != null && dateProp.CanWrite)
                {
                    dateProp.SetValue(vm, next);
                }

                // Try common "load date" method names (with DateTime param first, then parameterless).
                if (TryInvoke(vm, new[]
                    {
                        "LoadForDate",
                        "LoadDay",
                        "LoadDate",
                        "LoadLogsForDate",
                        "LoadLogs",
                        "ReloadForDate",
                        "ReloadDay",
                        "ReloadDate",
                        "RefreshForDate",
                        "RefreshDay",
                        "RefreshDate"
                    }, next))
                {
                    return;
                }

                // If nothing accepted a DateTime, try parameterless refresh/reload/load patterns.
                TryInvoke(vm, new[] { "Refresh", "Reload", "Load", "Initialize", "Init" });
            }
            catch
            {
                // swallow: this is a compatibility layer; we don't want crashes from reflection.
            }
        }

        private static PropertyInfo? FindDateProperty(object vm)
        {
            // Most common property names across your patch branches:
            var names = new[]
            {
                "SelectedDate",
                "CurrentDate",
                "LogDate",
                "DisplayDate",
                "Day",
                "CurrentDay",
                "ActiveDate",
                "SelectedDay"
            };

            var t = vm.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null) continue;

                // Support DateTime and Nullable<DateTime>
                if (p.PropertyType == typeof(DateTime) ||
                    p.PropertyType == typeof(DateTime?))
                    return p;
            }

            return null;
        }

        private static DateTime? GetDateFromProperty(object vm, PropertyInfo? dateProp)
        {
            if (dateProp == null) return null;

            var val = dateProp.GetValue(vm);

            // Pattern matching does not support nullable value types (DateTime?) directly.
            if (val is DateTime dt)
                return dt;

            // If the property is DateTime? and has a value, reflection returns boxed DateTime.
            // If it's null, val will be null.
            if (val == null)
                return null;

            // Last-resort conversion if something weird slips through.
            if (val is IConvertible)
            {
                try { return Convert.ToDateTime(val); } catch { }
            }

            return null;
        }


        private static bool TryInvoke(object target, string[] methodNames, params object[]? args)
        {
            var t = target.GetType();
            args ??= Array.Empty<object>();

            foreach (var name in methodNames)
            {
                // Look for exact match first
                var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                               .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                               .ToArray();

                foreach (var m in methods)
                {
                    var ps = m.GetParameters();

                    // Exact parameter count match
                    if (ps.Length != args.Length) continue;

                    // Check assignability (handles DateTime, Nullable<DateTime>, object, etc.)
                    bool ok = true;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (args[i] == null)
                        {
                            // null only ok for reference or nullable
                            if (ps[i].ParameterType.IsValueType &&
                                Nullable.GetUnderlyingType(ps[i].ParameterType) == null)
                            {
                                ok = false;
                                break;
                            }
                        }
                        else
                        {
                            var argType = args[i]!.GetType();
                            var paramType = ps[i].ParameterType;

                            if (!paramType.IsAssignableFrom(argType))
                            {
                                // allow DateTime -> DateTime?
                                if (paramType == typeof(DateTime?) && argType == typeof(DateTime))
                                    continue;

                                ok = false;
                                break;
                            }
                        }
                    }

                    if (!ok) continue;

                    try
                    {
                        m.Invoke(target, args);
                        return true;
                    }
                    catch
                    {
                        // try next overload/name
                    }
                }
            }

            return false;
        }
    }
}
