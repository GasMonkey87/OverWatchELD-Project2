using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OverWatchELD.Models;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    // D1/D2 + AA: Compliance banner + pre-trip violation logic (no clocks)
    public partial class ComplianceViewModel : ObservableObject
    {
        private string? _currentPath;
        private InspectionLog? _currentLog;

        private readonly DispatcherTimer _telemetryPollTimer;

        [ObservableProperty] private DateOnly selectedDateLocal = DateOnly.FromDateTime(DateTime.Now);
        [ObservableProperty] private string? loadId;

        [ObservableProperty] private string? loadNotes;
        [ObservableProperty] private string? dotNotes;

        [ObservableProperty] private string? truckId;
        [ObservableProperty] private string? trailerId;
        [ObservableProperty] private string? licensePlate;
        [ObservableProperty] private string? odometerMiles;
        [ObservableProperty] private string? engineHours;

        [ObservableProperty] private bool preTripCompleted;
        [ObservableProperty] private string? preTripNotes;

        [ObservableProperty] private bool postTripCompleted;
        [ObservableProperty] private string? postTripNotes;
        [ObservableProperty] private ObservableCollection<InspectionChecklistItem> preTripTractorChecklist = new();
        [ObservableProperty] private ObservableCollection<InspectionChecklistItem> preTripTrailerChecklist = new();
        [ObservableProperty] private ObservableCollection<InspectionChecklistItem> postTripTractorChecklist = new();
        [ObservableProperty] private ObservableCollection<InspectionChecklistItem> postTripTrailerChecklist = new();

        [ObservableProperty] private string statusLine = string.Empty;

        // AA banner
        [ObservableProperty] private string complianceBannerText = "COMPLIANT";
        [ObservableProperty] private string complianceBannerDetail = string.Empty;
        [ObservableProperty] private bool complianceIsViolation;

        public ObservableCollection<string> SavedLoadsForDay { get; } = new();

        public ComplianceViewModel()
        {
            // Timer for telemetry autofill (safe even if telemetry isn't available)
            _telemetryPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _telemetryPollTimer.Tick += (_, __) =>
            {
                TryAutofillFromTelemetry();
                EvaluateComplianceBanner();
            };
            _telemetryPollTimer.Start();

            LoadMostRecentForDay(SelectedDateLocal);
            RefreshSavedLoads();
            EvaluateComplianceBanner();
        }

        partial void OnSelectedDateLocalChanged(DateOnly value)
        {
            LoadMostRecentForDay(value);
            RefreshSavedLoads();
            EvaluateComplianceBanner();
        }

        partial void OnLoadIdChanged(string? value)
        {
            // When load id changes, re-evaluate banner and allow "save per load"
            EvaluateComplianceBanner();
        }

        partial void OnPreTripCompletedChanged(bool value) => EvaluateComplianceBanner();

        // ---------------- Commands ----------------

        [RelayCommand]
        private void NewForToday()
        {
            SelectedDateLocal = DateOnly.FromDateTime(DateTime.Now);
            var newLog = CreateDefaultLog(SelectedDateLocal, LoadId);
            ApplyLogToUi(newLog);
            _currentLog = newLog;
            _currentPath = null;
            StatusLine = "New inspection log created (not yet saved).";
            EvaluateComplianceBanner();
        }

        [RelayCommand]
        private void Save()
        {
            EnsureCurrentLogFromUi();

            if (_currentLog == null)
                return;

            // enforce "separate logs per day/load": include loadId in filename where possible
            var loadKey = string.IsNullOrWhiteSpace(LoadId) ? null : LoadId!.Trim();

            var saved = TrySaveViaStore(_currentLog, SelectedDateLocal, loadKey, _currentPath);
            if (!string.IsNullOrWhiteSpace(saved))
                _currentPath = saved;

            StatusLine = "Saved.";
            RefreshSavedLoads();
            EvaluateComplianceBanner();
        }

        [RelayCommand]
        private void MarkPreTripDone()
        {
            PreTripCompleted = true;
            EnsureCurrentLogFromUi();
            TrySetSectionCompleted(_currentLog, "PreTrip", true);
            StatusLine = "Pre-trip marked complete.";
            EvaluateComplianceBanner();
        }

        [RelayCommand]
        private void MarkPostTripDone()
        {
            PostTripCompleted = true;
            EnsureCurrentLogFromUi();
            TrySetSectionCompleted(_currentLog, "PostTrip", true);
            StatusLine = "Post-trip marked complete.";
            EvaluateComplianceBanner();
        }

        [RelayCommand]
        private void RefreshSavedLoads()
        {
            SavedLoadsForDay.Clear();

            // Try to ask store for load IDs for the selected day (reflection so we don't break builds)
            var t = typeof(InspectionStore);
            var day = SelectedDateLocal;

            string[]? ids = null;

            ids ??= InvokeStoreStringArray("ListLoadIdsForDate", day);
            ids ??= InvokeStoreStringArray("ListLoadIdsForDay", day);
            ids ??= InvokeStoreStringArray("ListLoadsForDay", day);
            ids ??= InvokeStoreStringArray("ListForDay", day);

            if (ids != null)
            {
                foreach (var id in ids.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
                    SavedLoadsForDay.Add(id);
            }
            else
            {
                // Fallback: at least show current load (if any)
                if (!string.IsNullOrWhiteSpace(LoadId))
                    SavedLoadsForDay.Add(LoadId!.Trim());
            }
        }

        [RelayCommand]
        private void LoadSelected(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            // Try load by day + load id (preferred)
            var loaded = TryLoadMostRecentForDayAndLoad(SelectedDateLocal, id.Trim());
            if (loaded.Log != null)
            {
                _currentPath = loaded.Path;
                _currentLog = loaded.Log;
                ApplyLogToUi(_currentLog);
                StatusLine = $"Loaded: {id}";
                EvaluateComplianceBanner();
                return;
            }

            // Fallback: treat selection as "most recent for day"
            LoadMostRecentForDay(SelectedDateLocal);
        }

        // ---------------- Core behavior ----------------

        private void LoadMostRecentForDay(DateOnly day)
        {
            try
            {
                var tuple = InspectionStore.LoadMostRecent(day); // returns (Path, Log)?
                if (tuple == null)
                {
                    StatusLine = "No saved logs found for this date.";
                    return;
                }

                // Nullable value tuple => use .Value to access fields
                _currentPath = tuple.Value.Path;
                _currentLog = tuple.Value.Log;

                ApplyLogToUi(_currentLog);
                StatusLine = string.IsNullOrWhiteSpace(_currentPath) ? "No saved log found for day." : "Loaded most recent log for day.";
            }
            catch
            {
                // If store throws or doesn't exist, fall back to a new log
                _currentPath = null;
                _currentLog = CreateDefaultLog(day, LoadId);
                ApplyLogToUi(_currentLog);
                StatusLine = "Started new log (store load failed).";
            }
        }

        private (string? Path, InspectionLog? Log) TryLoadMostRecentForDayAndLoad(DateOnly day, string loadId)
        {
            try
            {
                // Try store method: LoadMostRecent(day, loadId)
                var mi = typeof(InspectionStore).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "LoadMostRecent" && m.GetParameters().Length == 2);

                if (mi != null)
                {
                    var result = mi.Invoke(null, new object?[] { day, loadId });
                    if (result != null)
                    {
                        // expected: (string Path, InspectionLog Log)
                        var path = (string?)result.GetType().GetProperty("Path")?.GetValue(result);
                        var log = (InspectionLog?)result.GetType().GetProperty("Log")?.GetValue(result);
                        return (path, log);
                    }
                }

                // Try store method: LoadByDayAndLoad(day, loadId)
                mi = typeof(InspectionStore).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "LoadByDayAndLoad" && m.GetParameters().Length == 2);

                if (mi != null)
                {
                    var result = mi.Invoke(null, new object?[] { day, loadId });
                    if (result is InspectionLog il)
                        return (null, il);
                }
            }
            catch { }
            return (null, null);
        }

        private void ApplyLogToUi(InspectionLog? log)
        {
            if (log == null)
                return;

            // Prefer log's load id / notes if present
            LoadId = PreferExisting(GetStringProp(log, "LoadId"), LoadId);
            LoadNotes = PreferExisting(GetStringProp(log, "LoadNotes"), LoadNotes);
            DotNotes = PreferExisting(GetStringProp(log, "DotNotes"), DotNotes);

            TruckId = PreferExisting(GetStringProp(log, "UnitNumber"), TruckId);
            TrailerId = PreferExisting(GetStringProp(log, "TrailerNumber"), TrailerId);
            LicensePlate = PreferExisting(GetStringProp(log, "LicensePlate"), LicensePlate);
            OdometerMiles = PreferExisting(GetStringProp(log, "OdometerMiles"), OdometerMiles);
            EngineHours = PreferExisting(GetStringProp(log, "EngineHours"), EngineHours);

            // Pre/Post sections
            PreTripNotes = PreferExisting(GetSectionNotes(log, "PreTrip"), PreTripNotes);
            PostTripNotes = PreferExisting(GetSectionNotes(log, "PostTrip"), PostTripNotes);
            // Checklists (tractor/trailer) - keep them stable per log
            var defaults = InspectionLog.CreateDefault(log.LocalDay, log.LoadId);
            PreTripTractorChecklist = new ObservableCollection<InspectionChecklistItem>(
                (log.PreTripTractorChecklist != null && log.PreTripTractorChecklist.Count > 0) ? log.PreTripTractorChecklist : defaults.PreTripTractorChecklist);
            PreTripTrailerChecklist = new ObservableCollection<InspectionChecklistItem>(
                (log.PreTripTrailerChecklist != null && log.PreTripTrailerChecklist.Count > 0) ? log.PreTripTrailerChecklist : defaults.PreTripTrailerChecklist);
            PostTripTractorChecklist = new ObservableCollection<InspectionChecklistItem>(
                (log.PostTripTractorChecklist != null && log.PostTripTractorChecklist.Count > 0) ? log.PostTripTractorChecklist : defaults.PostTripTractorChecklist);
            PostTripTrailerChecklist = new ObservableCollection<InspectionChecklistItem>(
                (log.PostTripTrailerChecklist != null && log.PostTripTrailerChecklist.Count > 0) ? log.PostTripTrailerChecklist : defaults.PostTripTrailerChecklist);

            PreTripCompleted = GetSectionCompleted(log, "PreTrip") || PreTripCompleted;
            PostTripCompleted = GetSectionCompleted(log, "PostTrip") || PostTripCompleted;

            TryAutofillFromTelemetry();
        }

        private void EnsureCurrentLogFromUi()
        {
            if (_currentLog == null)
                _currentLog = CreateDefaultLog(SelectedDateLocal, LoadId);

            var log = _currentLog;

            SetStringProp(log, "LoadId", LoadId);
            SetStringProp(log, "LoadNotes", LoadNotes);
            SetStringProp(log, "DotNotes", DotNotes);

            SetStringProp(log, "UnitNumber", TruckId);
            SetStringProp(log, "TrailerNumber", TrailerId);
            SetStringProp(log, "LicensePlate", LicensePlate);
            SetStringProp(log, "OdometerMiles", OdometerMiles);
            SetStringProp(log, "EngineHours", EngineHours);

            TrySetSectionNotes(log, "PreTrip", PreTripNotes);
            TrySetSectionNotes(log, "PostTrip", PostTripNotes);

            TrySetSectionCompleted(log, "PreTrip", PreTripCompleted);
            TrySetSectionCompleted(log, "PostTrip", PostTripCompleted);

            // If the log has LocalDay / Day etc, keep it in sync
            TrySetDateOnlyProp(log, "LocalDay", SelectedDateLocal);
            TrySetDateOnlyProp(log, "Day", SelectedDateLocal);

            // Persist detailed tractor/trailer checklists (if present on the model)
            TrySetChecklist(log, "PreTripTractorChecklist", PreTripTractorChecklist);
            TrySetChecklist(log, "PreTripTrailerChecklist", PreTripTrailerChecklist);
            TrySetChecklist(log, "PostTripTractorChecklist", PostTripTractorChecklist);
            TrySetChecklist(log, "PostTripTrailerChecklist", PostTripTrailerChecklist);
        }

        // ---------------- AA banner + violation logic ----------------

        private void EvaluateComplianceBanner()
        {
            // Rule: Pre-trip must be done at start of day (today) or you're in violation.
            var isToday = SelectedDateLocal == DateOnly.FromDateTime(DateTime.Now);

            // If we can read completion from the log section, use it; otherwise use VM checkbox.
            var preDone = PreTripCompleted;
            if (_currentLog != null)
                preDone = preDone || GetSectionCompleted(_currentLog, "PreTrip");

            var violation = isToday && !preDone;

            ComplianceIsViolation = violation;

            if (violation)
            {
                ComplianceBannerText = "VIOLATION";
                ComplianceBannerDetail = "Pre-trip inspection required to start the day.";
                StatusLine = "Pre-trip not completed for today.";
            }
            else
            {
                ComplianceBannerText = "COMPLIANT";
                ComplianceBannerDetail = string.Empty;
            }
        }

        // ---------------- Telemetry autofill (best-effort) ----------------

        private void TryAutofillFromTelemetry()
        {
            // Many projects expose telemetry differently. We try common patterns safely.
            try
            {
                object? snap = null;

                // EldEngine.LastTelemetry
                snap = typeof(EldEngine).GetProperty("LastTelemetry", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

                // EldEngine.CurrentTelemetry / TelemetrySnapshot
                snap ??= typeof(EldEngine).GetProperty("CurrentTelemetry", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                snap ??= typeof(EldEngine).GetProperty("TelemetrySnapshot", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

                if (snap == null) return;

                TruckId = PreferExisting(GetStringProp(snap, "TruckId") ?? GetStringProp(snap, "UnitNumber"), TruckId);
                TrailerId = PreferExisting(GetStringProp(snap, "TrailerId") ?? GetStringProp(snap, "TrailerNumber"), TrailerId);
                LicensePlate = PreferExisting(GetStringProp(snap, "LicensePlate"), LicensePlate);

                var odo = GetStringProp(snap, "OdometerMiles") ?? GetStringProp(snap, "Odometer");
                if (!string.IsNullOrWhiteSpace(odo))
                    OdometerMiles = PreferExisting(odo, OdometerMiles);

                var hrs = GetStringProp(snap, "EngineHours");
                if (!string.IsNullOrWhiteSpace(hrs))
                    EngineHours = PreferExisting(hrs, EngineHours);
            }
            catch { /* ignore */ }
        }

        // ---------------- Reflection helpers ----------------

        private static InspectionLog CreateDefaultLog(DateOnly day, string? loadId)
        {
            // Your project defines: InspectionLog.CreateDefault(DateOnly day, string? loadId)
            try
            {
                return InspectionLog.CreateDefault(day, loadId);
            }
            catch
            {
                // Fallback: try parameterless
                var mi = typeof(InspectionLog).GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static, Type.DefaultBinder, Type.EmptyTypes, null);
                if (mi != null && mi.Invoke(null, null) is InspectionLog il)
                    return il;

                // Last resort
                return Activator.CreateInstance<InspectionLog>();
            }
        }

        private static string? TrySaveViaStore(InspectionLog log, DateOnly day, string? loadId, string? currentPath)
        {
            try
            {
                var t = typeof(InspectionStore);

                // Prefer: Save(DateOnly day, string? loadId, InspectionLog log)
                var mi = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Save" && m.GetParameters().Length == 3);
                if (mi != null)
                {
                    var path = mi.Invoke(null, new object?[] { day, loadId, log }) as string;
                    return path ?? currentPath;
                }

                // Alternate: Save(string path, InspectionLog log)
                mi = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Save" && m.GetParameters().Length == 2);
                if (mi != null)
                {
                    var p0 = mi.GetParameters()[0].ParameterType;
                    if (p0 == typeof(string))
                    {
                        mi.Invoke(null, new object?[] { currentPath, log });
                        return currentPath;
                    }
                }

                // Alternate: Save(InspectionLog log)
                mi = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Save" && m.GetParameters().Length == 1);
                if (mi != null)
                {
                    mi.Invoke(null, new object?[] { log });
                    return currentPath;
                }
            }
            catch { }
            return currentPath;
        }

        private static string[]? InvokeStoreStringArray(string method, DateOnly day)
        {
            try
            {
                var mi = typeof(InspectionStore).GetMethod(method, BindingFlags.Public | BindingFlags.Static, new[] { typeof(DateOnly) });
                if (mi != null)
                {
                    var res = mi.Invoke(null, new object?[] { day });
                    if (res is string[] a) return a;
                    if (res is System.Collections.Generic.IEnumerable<string> e) return e.ToArray();
                }
            }
            catch { }
            return null;
        }

        private static string PreferExisting(string? candidate, string? existing)
            => !string.IsNullOrWhiteSpace(existing) ? existing! : (candidate ?? existing ?? string.Empty);

        private static string? GetStringProp(object obj, string propName)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null) return null;
            var val = pi.GetValue(obj);
            return val?.ToString();
        }

        private static void SetStringProp(object obj, string propName, string? value)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite) return;

            if (pi.PropertyType == typeof(string))
            {
                pi.SetValue(obj, value);
                return;
            }

            // handle numbers stored as string/double/etc
            try
            {
                if (pi.PropertyType == typeof(double?) || pi.PropertyType == typeof(double))
                {
                    if (double.TryParse(value, out var d))
                        pi.SetValue(obj, d);
                    return;
                }
            }
            catch { }
        }

        private static void TrySetDateOnlyProp(object obj, string propName, DateOnly value)
        {
            try
            {
                var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null || !pi.CanWrite) return;
                if (pi.PropertyType == typeof(DateOnly))
                    pi.SetValue(obj, value);
            }
            catch { }
        }

        private static object? GetSection(object log, string sectionName)
        {
            return log.GetType().GetProperty(sectionName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(log);
        }

        private static bool GetSectionCompleted(object log, string sectionName)
        {
            try
            {
                var section = GetSection(log, sectionName);
                if (section == null) return false;

                var t = section.GetType();

                foreach (var name in new[] { "Completed", "IsCompleted", "IsDone", "Done" })
                {
                    var pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (pi != null && (pi.PropertyType == typeof(bool) || pi.PropertyType == typeof(bool?)))
                    {
                        var v = pi.GetValue(section);
                        return v is bool b && b;
                    }
                }

                // If it has Items (IEnumerable), consider complete if all items are checked/ok
                var itemsPi = t.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
                if (itemsPi?.GetValue(section) is System.Collections.IEnumerable items)
                {
                    bool any = false;
                    foreach (var it in items)
                    {
                        any = true;
                        var ok = GetBoolProp(it, "IsOk") ?? GetBoolProp(it, "Checked") ?? GetBoolProp(it, "Completed") ?? GetBoolProp(it, "IsCompleted");
                        if (ok == false) return false;
                    }
                    return any; // if at least one item and none are false
                }
            }
            catch { }
            return false;
        }

        private static bool? GetBoolProp(object obj, string propName)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null) return null;
            var v = pi.GetValue(obj);
            // Note: a boxed Nullable<bool> with a value boxes as a bool.
            if (v is bool b) return b;
            return null;
        }

        private static string? GetSectionNotes(object log, string sectionName)
        {
            try
            {
                var section = GetSection(log, sectionName);
                if (section == null) return null;

                foreach (var name in new[] { "Notes", "Comment", "Comments", "Text" })
                {
                    var pi = section.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (pi != null)
                        return pi.GetValue(section)?.ToString();
                }
            }
            catch { }
            return null;
        }

        private static void TrySetSectionNotes(object log, string sectionName, string? notes)
        {
            try
            {
                var section = GetSection(log, sectionName);
                if (section == null) return;

                foreach (var name in new[] { "Notes", "Comment", "Comments", "Text" })
                {
                    var pi = section.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (pi != null && pi.CanWrite && pi.PropertyType == typeof(string))
                    {
                        pi.SetValue(section, notes);
                        return;
                    }
                }
            }
            catch { }
        }

        private static void TrySetSectionCompleted(object? log, string sectionName, bool completed)
        {
            if (log == null) return;
            try
            {
                var section = GetSection(log, sectionName);
                if (section == null) return;

                foreach (var name in new[] { "Completed", "IsCompleted", "IsDone", "Done" })
                {
                    var pi = section.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (pi != null && pi.CanWrite && (pi.PropertyType == typeof(bool) || pi.PropertyType == typeof(bool?)))
                    {
                        pi.SetValue(section, completed);
                        return;
                    }
                }
            }
            catch { }
        }

        private static void TrySetChecklist(object? log, string propName, ObservableCollection<InspectionChecklistItem>? items)
        {
            if (log == null) return;
            try
            {
                var pi = log.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null || !pi.CanWrite) return;

                var list = items?.ToList() ?? new List<InspectionChecklistItem>();

                if (pi.PropertyType == typeof(List<InspectionChecklistItem>))
                {
                    pi.SetValue(log, list);
                    return;
                }

                if (pi.PropertyType.IsArray && pi.PropertyType.GetElementType() == typeof(InspectionChecklistItem))
                {
                    pi.SetValue(log, list.ToArray());
                    return;
                }

                if (pi.PropertyType.IsAssignableFrom(list.GetType()))
                {
                    pi.SetValue(log, list);
                }
            }
            catch { }
        }

    }
}