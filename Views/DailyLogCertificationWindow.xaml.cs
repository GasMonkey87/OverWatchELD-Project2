using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using OverWatchELD.Services;
using OverWatchELD.Models;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class DailyLogCertificationWindow : Window
    {
        private readonly object? _vm;

        public DailyLogCertificationWindow()
        {
            InitializeComponent();
            _vm = new LogsViewModel();
            DataContext = _vm;

            Loaded += delegate
            {
                HookVmPropertyChanged(_vm);
                ForceLoadUnsignedDaysFromDbIntoVm();
                BindDriverDropDown();
                RecomputeCanSign();
            };
        }

        // 1-arg ctor used by LogsView
        public DailyLogCertificationWindow(object vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            Loaded += delegate
            {
                HookVmPropertyChanged(vm);
                ForceLoadUnsignedDaysFromDbIntoVm();
                BindDriverDropDown();
                RecomputeCanSign();
            };
        }

        private object? Vm => _vm ?? DataContext;

        private void HookVmPropertyChanged(object vm)
        {
            try
            {
                if (vm is INotifyPropertyChanged npc)
                    npc.PropertyChanged += delegate { RecomputeCanSign(); };
            }
            catch
            {
            }
        }

        // =========================
        // 🔒 FORCE MULTI-DAY LOAD
        // =========================
        private void ForceLoadUnsignedDaysFromDbIntoVm()
        {
            try
            {
                var target = Vm;
                if (target == null) return;

                DatabaseService.Initialize();

                var todayLocal = EldClock.UtcNow.ToLocalTime().Date;
                var start = todayLocal.AddDays(-60);
                var end = todayLocal;

                var daysWithActivity = DatabaseService
                    .GetLocalDatesWithAnyActivity(start, end)
                    .Select(d => d.Date)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToList();

                // Strong path: LogsViewModel
                if (target is LogsViewModel lvm)
                {
                    lvm.UnsignedDays.Clear();

                    foreach (var d in daysWithActivity)
                    {
                        var cert = DatabaseService.GetLogCertification(d);

                        lvm.UnsignedDays.Add(new UnsignedDayVm(
                            d,
                            cert?.Certified == true,
                            cert?.DriverName,
                            cert?.SignedAtUtc));
                    }

                    RecomputeCanSign();
                    return;
                }

                // Reflection fallback
                var pi = target.GetType().GetProperty("UnsignedDays",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (pi == null) return;

                var listObj = pi.GetValue(target);
                if (listObj is not IEnumerable) return;

                var clearMi = listObj.GetType().GetMethod("Clear");
                var addMi = listObj.GetType().GetMethod("Add");
                if (clearMi == null || addMi == null) return;

                clearMi.Invoke(listObj, null);

                var rowType =
                    Type.GetType("OverWatchELD.ViewModels.UnsignedDayVm, OverWatchELD") ??
                    Type.GetType("OverWatchELD.ViewModels.UnsignedDayVm, ATS_ELD");

                foreach (var d in daysWithActivity)
                {
                    var cert = DatabaseService.GetLogCertification(d);

                    object row = rowType != null
                        ? Activator.CreateInstance(
                            rowType,
                            d,
                            cert?.Certified == true,
                            cert?.DriverName,
                            cert?.SignedAtUtc)!
                        : d.ToString("yyyy-MM-dd");

                    TrySetProperty(row, "IsSelected", false);
                    addMi.Invoke(listObj, new[] { row });
                }

                RecomputeCanSign();
            }
            catch
            {
                // never crash certification window
            }
        }

        private void BindDriverDropDown()
        {
            try
            {
                DriverDropdownService.Bind(DriverNameBox, GetVmString("DriverName"), includeUnassigned: false);
            }
            catch
            {
            }
        }

        private string GetSelectedDriverName()
        {
            try
            {
                return DriverDropdownService.SelectedName(DriverNameBox, GetVmString("DriverName")).Trim();
            }
            catch
            {
                return GetVmString("DriverName").Trim();
            }
        }

        // =========================
        // UI / SIGN LOGIC
        // =========================
        private void Input_Changed(object sender, RoutedEventArgs e) => RecomputeCanSign();

        private void RecomputeCanSign()
        {
            try
            {
                string driver = GetSelectedDriverName();
                string signature = (SignatureBox?.Text ?? GetVmString("Signature")).Trim();
                bool certified = CertifyCheck?.IsChecked == true || GetVmBool("Certified");

                TrySetVmProperty("DriverName", driver);
                TrySetVmProperty("Signature", signature);
                TrySetVmProperty("Certified", certified);

                bool canBase =
                    certified &&
                    driver.Length > 0 &&
                    signature.Length > 0 &&
                    string.Equals(driver, signature, StringComparison.OrdinalIgnoreCase);

                if (TryGetVmBool("IsCertificationBlocked", out bool blocked) && blocked)
                    canBase = false;

                TrySetVmProperty("CanSignFocusedDay", canBase);
                TrySetVmProperty("CanSignSelectedDays", canBase && HasSelectedUnsignedDays());
            }
            catch
            {
            }
        }

        private bool HasSelectedUnsignedDays()
        {
            try
            {
                var pi = Vm?.GetType().GetProperty("UnsignedDays",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var list = pi?.GetValue(Vm) as IEnumerable;
                if (list == null) return false;

                foreach (var item in list)
                {
                    if (item == null) continue;

                    // Only unsigned days should count for sign-selected
                    var certPi = item.GetType().GetProperty("IsCertified");
                    if (certPi?.GetValue(item) is bool isCertified && isCertified)
                        continue;

                    var selPi = item.GetType().GetProperty("IsSelected");
                    if (selPi?.GetValue(item) is bool b && b)
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // =========================
        // BUTTONS
        // =========================
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Sign_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateBeforeSign(out var err))
            {
                MessageBox.Show(err, "Cannot Sign");
                return;
            }

            NormalizeForCertificationFromVm(false);

            try
            {
                var driver = GetSelectedDriverName();
                var signature = (SignatureBox?.Text ?? GetVmString("Signature")).Trim();

                // Strong path: persist through LogsViewModel
                if (Vm is LogsViewModel lvm)
                {
                    lvm.CertifySelectedDay(driver, signature);
                    DialogResult = true;
                    Close();
                    return;
                }

                // Reflection fallback
                if (TryInvokeAny("CertifySelectedDay"))
                {
                    DialogResult = true;
                    Close();
                    return;
                }

                // Last resort: direct DB write for focused/selected date
                var selectedDate = TryGetVmDate("SelectedDate")?.Date ?? EldClock.LocalNow.Date;
                UpsertDayCertification(selectedDate, driver, signature);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to certify log: " + ex.Message, "Cannot Sign");
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pi = Vm?.GetType().GetProperty("UnsignedDays",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var list = pi?.GetValue(Vm) as IEnumerable;
                if (list == null) return;

                foreach (var item in list)
                {
                    if (item == null) continue;

                    var certPi = item.GetType().GetProperty("IsCertified");
                    if (certPi?.GetValue(item) is bool isCertified && isCertified)
                        continue;

                    var selPi = item.GetType().GetProperty("IsSelected",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (selPi != null && selPi.CanWrite)
                        selPi.SetValue(item, true);
                }
            }
            catch
            {
            }

            RecomputeCanSign();
        }

        private async void ExportDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as LogsViewModel ?? _vm;

                var ok = await new DiscordLogExportService().ExportAsync(DataContext as LogsViewModel);

                MessageBox.Show(
                    ok ? "Daily log exported to Discord." : "Daily log export failed.",
                    "Export Logs",
                    MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.GetBaseException().Message,
                    "Export Logs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pi = Vm?.GetType().GetProperty("UnsignedDays",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var list = pi?.GetValue(Vm) as IEnumerable;
                if (list == null) return;

                foreach (var item in list)
                {
                    if (item == null) continue;

                    var selPi = item.GetType().GetProperty("IsSelected",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (selPi != null && selPi.CanWrite)
                        selPi.SetValue(item, false);
                }
            }
            catch
            {
            }

            RecomputeCanSign();
        }

        private void SignSelectedDays_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateBeforeSign(out var err))
            {
                MessageBox.Show(err, "Cannot Sign");
                return;
            }

            if (!HasSelectedUnsignedDays())
            {
                MessageBox.Show("Select one or more unsigned days.");
                return;
            }

            NormalizeForCertificationFromVm(true);

            try
            {
                var driver = GetSelectedDriverName();
                var signature = (SignatureBox?.Text ?? GetVmString("Signature")).Trim();

                var dates = GetSelectedUnsignedDates();
                foreach (var d in dates)
                    UpsertDayCertification(d, driver, signature);

                // Refresh VM view if possible
                if (Vm is LogsViewModel lvm)
                    lvm.Refresh();
                else
                    ForceLoadUnsignedDaysFromDbIntoVm();

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to certify selected days: " + ex.Message, "Cannot Sign");
            }
        }

        // =========================
        // HELPERS
        // =========================
        private string GetVmString(string prop) =>
            Vm?.GetType().GetProperty(prop)?.GetValue(Vm)?.ToString() ?? "";

        private bool GetVmBool(string prop) =>
            Vm?.GetType().GetProperty(prop)?.GetValue(Vm) as bool? ?? false;

        private bool TryGetVmBool(string prop, out bool value)
        {
            value = GetVmBool(prop);
            return true;
        }

        private DateTime? TryGetVmDate(string prop)
        {
            try
            {
                var value = Vm?.GetType().GetProperty(prop)?.GetValue(Vm);
                if (value is DateTime dt) return dt;
                if (value is DateTimeOffset dto) return dto.Date;
            }
            catch
            {
            }

            return null;
        }

        private void TrySetVmProperty(string prop, object value)
        {
            try
            {
                Vm?.GetType().GetProperty(prop)?.SetValue(Vm, value);
            }
            catch
            {
            }
        }

        private void TrySetProperty(object obj, string prop, object value)
        {
            try
            {
                obj.GetType().GetProperty(prop)?.SetValue(obj, value);
            }
            catch
            {
            }
        }

        private bool TryInvokeAny(params string[] names)
        {
            foreach (var n in names)
            {
                try
                {
                    var mi = Vm?.GetType().GetMethod(n);
                    if (mi != null)
                    {
                        var parameters = mi.GetParameters();
                        if (parameters.Length == 0)
                        {
                            mi.Invoke(Vm, null);
                            return true;
                        }
                    }
                }
                catch
                {
                }
            }
            return false;
        }

        private List<DateTime> GetSelectedUnsignedDates()
        {
            var result = new List<DateTime>();

            try
            {
                var pi = Vm?.GetType().GetProperty("UnsignedDays",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var list = pi?.GetValue(Vm) as IEnumerable;
                if (list == null) return result;

                foreach (var item in list)
                {
                    if (item == null) continue;

                    var certPi = item.GetType().GetProperty("IsCertified");
                    if (certPi?.GetValue(item) is bool isCertified && isCertified)
                        continue;

                    var selPi = item.GetType().GetProperty("IsSelected");
                    var datePi = item.GetType().GetProperty("DateLocal");

                    if (selPi?.GetValue(item) is bool isSelected &&
                        isSelected &&
                        datePi?.GetValue(item) is DateTime d)
                    {
                        result.Add(d.Date);
                    }
                }
            }
            catch
            {
            }

            return result.Distinct().OrderBy(d => d).ToList();
        }

        private void UpsertDayCertification(DateTime localDate, string driverName, string signature)
        {
            DatabaseService.Initialize();

            var cert = new DailyLogCertification
            {
                LogDateLocal = localDate.Date.ToString("yyyy-MM-dd"),
                SignedAtUtc = DateTimeOffset.UtcNow,
                DriverName = string.IsNullOrWhiteSpace(driverName) ? "Driver" : driverName.Trim(),
                Signature = string.IsNullOrWhiteSpace(signature) ? driverName?.Trim() ?? "Driver" : signature.Trim(),
                Certified = true,
                CertificationText = "I hereby certify that my data entries and my record of duty status for this 24-hour period are true and correct."
            };

            DatabaseService.UpsertLogCertification(cert);
        }

        private bool ValidateBeforeSign(out string error)
        {
            error = "";

            var driver = GetSelectedDriverName();
            var signature = (SignatureBox?.Text ?? GetVmString("Signature")).Trim();

            if (CertifyCheck?.IsChecked != true)
            {
                error = "Please certify before signing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(driver))
            {
                error = "Enter the driver's name.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                error = "Enter the signature.";
                return false;
            }

            if (!string.Equals(driver, signature, StringComparison.OrdinalIgnoreCase))
            {
                error = "Driver name and signature must match.";
                return false;
            }

            return true;
        }

        private void NormalizeForCertificationFromVm(bool selectedDays)
        {
            TryInvokeAny(
                selectedDays ? "NormalizeSelectedDaysForCertification" : "NormalizeForCertification",
                "NormalizeDays",
                "SealDayGaps"
            );
        }
    }
}