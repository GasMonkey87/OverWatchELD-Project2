using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

using OverWatchELD.Models;

namespace OverWatchELD.Views
{
    public partial class EditDutyDialog : Window
    {
        private readonly Vm _vm;

        public EditDutyDialog()
        {
            InitializeComponent();
            _vm = new Vm(this);
            DataContext = _vm;
        }

        // ✅ LogsView calls this
        public EditDutyDialog(DutyEvent model) : this()
        {
            _vm.LoadFromModel(model);
        }

        // -------------------------
        // ViewModel
        // -------------------------
        private sealed class Vm : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

            private readonly Window _owner;
            private DutyEvent? _model;

            private DateTime _startLocal;
            private DateTime _endLocal;
            private DutyStatus _selectedStatus;
            private string _noteText = "";
            private string _errorText = "";

            private bool _startWasUtc;
            private bool _endWasUtc;

            private const string DateFmt = "yyyy-MM-dd HH:mm";

            public Vm(Window owner)
            {
                _owner = owner;

                AllowedStatuses = new List<DutyStatus>
                {
                    DutyStatus.OffDuty,
                    DutyStatus.Sleeper,
                    DutyStatus.Driving,
                    DutyStatus.OnDuty,
                    DutyStatus.PersonalConveyance,
                    DutyStatus.YardMove
                };

                CancelCommand = new SimpleCommand(_ => Cancel());
                SaveCommand = new SimpleCommand(_ => Save());
            }

            // Bound in XAML
            public string HeaderText => "Edit Duty Segment (DOT Annotation Required)";

            public IList<DutyStatus> AllowedStatuses { get; }

            public DutyStatus SelectedStatus
            {
                get => _selectedStatus;
                set
                {
                    if (_selectedStatus == value) return;
                    _selectedStatus = value;
                    OnChanged(nameof(SelectedStatus));
                }
            }

            public string StartLocalText => _startLocal.ToString(DateFmt, CultureInfo.CurrentCulture);

            public string EndLocalText => _endLocal.ToString(DateFmt, CultureInfo.CurrentCulture);

            public string NoteText
            {
                get => _noteText;
                set
                {
                    var v = value ?? "";
                    if (_noteText == v) return;
                    _noteText = v;
                    if (!string.IsNullOrWhiteSpace(_noteText))
                        ErrorText = ""; // clear error as user types
                    OnChanged(nameof(NoteText));
                }
            }

            public string ErrorText
            {
                get => _errorText;
                private set
                {
                    if (_errorText == value) return;
                    _errorText = value;
                    OnChanged(nameof(ErrorText));
                }
            }

            public ICommand CancelCommand { get; }
            public ICommand SaveCommand { get; }

            public void LoadFromModel(DutyEvent model)
            {
                _model = model ?? throw new ArgumentNullException(nameof(model));

                // Read start/end (support many possible property names)
                var (s, sUtc) = ReadDateTime(_model,
                    "StartLocal", "StartTimeLocal", "StartTime", "Start", "StartAt",
                    "StartUtc", "StartTimeUtc", "StartTimeUTC", "StartUTC");

                var (e, eUtc) = ReadDateTime(_model,
                    "EndLocal", "EndTimeLocal", "EndTime", "End", "EndAt",
                    "EndUtc", "EndTimeUtc", "EndTimeUTC", "EndUTC");

                _startWasUtc = sUtc;
                _endWasUtc = eUtc;

                _startLocal = s == default ? DateTime.Now : s;
                _endLocal = e == default ? _startLocal : e;

                SelectedStatus = ReadStatus(_model, "Status", "DutyStatus", "Duty", "State");

                // Existing note/remark (if any)
                NoteText = ReadString(_model,
                    "Note", "Notes", "Remark", "Remarks", "Comment", "Comments", "Annotation", "Reason", "Description") ?? "";

                // Notify start/end text bindings
                OnChanged(nameof(StartLocalText));
                OnChanged(nameof(EndLocalText));

                // Optional: show more context in header if you want
                // HeaderText is currently constant to match XAML title.
            }

            private void Cancel()
            {
                if (_owner is Window w)
                {
                    w.DialogResult = false;
                    w.Close();
                }
            }

            private void Save()
            {
                if (_model == null)
                {
                    ErrorText = "No duty event loaded.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(NoteText))
                {
                    ErrorText = "DOT Annotation / Reason is required.";
                    return;
                }

                ErrorText = "";

                // Write status + note back
                WriteEnum(_model, SelectedStatus, "Status", "DutyStatus", "Duty", "State");
                WriteString(_model, NoteText,
                    "Note", "Notes", "Remark", "Remarks", "Comment", "Comments", "Annotation", "Reason", "Description");

                // Preserve the original time fields (read-only in UI). Still re-write to keep consistency.
                WriteDateTime(_model, _startWasUtc, _startLocal,
                    "StartLocal", "StartTimeLocal", "StartTime", "Start", "StartAt",
                    "StartUtc", "StartTimeUtc", "StartTimeUTC", "StartUTC");

                WriteDateTime(_model, _endWasUtc, _endLocal,
                    "EndLocal", "EndTimeLocal", "EndTime", "End", "EndAt",
                    "EndUtc", "EndTimeUtc", "EndTimeUTC", "EndUTC");

                if (_owner is Window w)
                {
                    w.DialogResult = true;
                    w.Close();
                }
            }

            private void OnChanged(string name) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            // -------------------------
            // Reflection helpers
            // -------------------------
            private static (DateTime valueLocal, bool wasUtc) ReadDateTime(object obj, params string[] candidates)
            {
                foreach (var name in candidates)
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                    if (p == null || !p.CanRead) continue;

                    var raw = p.GetValue(obj);
                    if (raw == null) continue;

                    if (raw is DateTime dt)
                    {
                        var nameUtc = name.IndexOf("utc", StringComparison.OrdinalIgnoreCase) >= 0;
                        var wasUtc = nameUtc || dt.Kind == DateTimeKind.Utc;

                        var local = wasUtc ? DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime() : dt;
                        return (local, wasUtc);
                    }

                    if (raw is DateTimeOffset dto)
                    {
                        return (dto.LocalDateTime, true);
                    }
                }

                return (default, false);
            }

            private static void WriteDateTime(object obj, bool storeUtc, DateTime localValue, params string[] candidates)
            {
                foreach (var name in candidates)
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                    if (p == null || !p.CanWrite) continue;

                    var targetType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

                    if (targetType == typeof(DateTime))
                    {
                        var dt = localValue;
                        if (storeUtc || name.IndexOf("utc", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            dt = DateTime.SpecifyKind(localValue, DateTimeKind.Local).ToUniversalTime();
                            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                        }
                        p.SetValue(obj, dt);
                        return;
                    }

                    if (targetType == typeof(DateTimeOffset))
                    {
                        var dto = (storeUtc || name.IndexOf("utc", StringComparison.OrdinalIgnoreCase) >= 0)
                            ? new DateTimeOffset(DateTime.SpecifyKind(localValue, DateTimeKind.Local).ToUniversalTime(), TimeSpan.Zero)
                            : new DateTimeOffset(localValue);
                        p.SetValue(obj, dto);
                        return;
                    }
                }
            }

            private static DutyStatus ReadStatus(object obj, params string[] candidates)
            {
                foreach (var name in candidates)
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                    if (p == null || !p.CanRead) continue;

                    var raw = p.GetValue(obj);
                    if (raw == null) continue;

                    if (raw is DutyStatus ds) return ds;

                    if (raw is string s && Enum.TryParse<DutyStatus>(s, true, out var parsed)) return parsed;
                    if (raw is int i && Enum.IsDefined(typeof(DutyStatus), i)) return (DutyStatus)i;
                }

                return DutyStatus.OffDuty;
            }

            private static string? ReadString(object obj, params string[] candidates)
            {
                foreach (var name in candidates)
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                    if (p == null || !p.CanRead) continue;

                    var raw = p.GetValue(obj);
                    if (raw == null) continue;

                    return raw.ToString();
                }
                return null;
            }

            private static void WriteString(object obj, string value, params string[] candidates)
            {
                foreach (var name in candidates)
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                    if (p == null || !p.CanWrite) continue;

                    var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                    if (t == typeof(string))
                    {
                        p.SetValue(obj, value);
                        return;
                    }
                }
            }

            private static void WriteEnum(object obj, DutyStatus value, params string[] candidates)
            {
                foreach (var name in candidates)
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                    if (p == null || !p.CanWrite) continue;

                    var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

                    if (t == typeof(DutyStatus))
                    {
                        p.SetValue(obj, value);
                        return;
                    }

                    if (t == typeof(string))
                    {
                        p.SetValue(obj, value.ToString());
                        return;
                    }

                    if (t == typeof(int))
                    {
                        p.SetValue(obj, (int)value);
                        return;
                    }
                }
            }
        }

        // Tiny ICommand helper so we don't depend on your RelayCommand implementation
        private sealed class SimpleCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Func<object?, bool>? _canExecute;

            public SimpleCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

            public void Execute(object? parameter) => _execute(parameter);

            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
