using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using OverWatchELD.Services;
using OverWatchELD.Views;

namespace OverWatchELD.ViewModels
{
    /// <summary>
    /// Support tab VM
    /// ✅ Matches all bindings in Views/SupportView.xaml
    /// ✅ Direct SMTP email send (no mailto fallback)
    /// ✅ Includes sender email via Reply-To
    /// ✅ Saves local ticket history
    /// ✅ Attaches diagnostics text file when enabled
    /// </summary>
    public sealed class SupportViewModel : INotifyPropertyChanged
    {
        private const string TicketEmail = "GasMonkeyCreations@gmail.com";
        private const string SmtpHost = "smtp.gmail.com";
        private const int SmtpPort = 587;
        private const string SmtpPasswordEnvVar = "OWELD_SMTP_PASS";

        private static string AppDataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OverWatchELD");

        private static string SettingsPath => Path.Combine(AppDataDir, "settings.json");
        private static string TicketHistoryPath => Path.Combine(AppDataDir, "support_tickets.json");
        private static string DiagnosticsDir => Path.Combine(AppDataDir, "SupportDiagnostics");

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private sealed class SupportTicketRecord
        {
            public string TicketId { get; set; } = "";
            public string SenderEmail { get; set; } = "";
            public string Category { get; set; } = "";
            public string Severity { get; set; } = "";
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public bool IncludeDiagnostics { get; set; }
            public string DiagnosticsPath { get; set; } = "";
            public string CreatedLocal { get; set; } = "";
            public string Result { get; set; } = "";
        }

        // ---------------- Support Status ----------------

        private string _statusReport = "";
        public string StatusReport { get => _statusReport; private set { _statusReport = value; OnPropertyChanged(); } }

        private string _statusLine = "";
        public string StatusLine { get => _statusLine; private set { _statusLine = value; OnPropertyChanged(); } }

        // ---------------- Ticket fields (Report Issue card) ----------------

        public ObservableCollection<string> Categories { get; } = new()
        {
            "Bug / UI",
            "Crash / Startup",
            "Telemetry / Connection",
            "Messaging / Dispatch",
            "Other"
        };

        public ObservableCollection<string> Severities { get; } = new()
        {
            "Low",
            "Medium",
            "High",
            "Critical"
        };

        private string _selectedCategory = "Bug / UI";
        public string SelectedCategory { get => _selectedCategory; set { _selectedCategory = value; OnPropertyChanged(); } }

        private string _selectedSeverity = "Medium";
        public string SelectedSeverity { get => _selectedSeverity; set { _selectedSeverity = value; OnPropertyChanged(); } }

        private string _senderEmail = "";
        public string SenderEmail
        {
            get => _senderEmail;
            set { _senderEmail = value; OnPropertyChanged(); }
        }

        private string _ticketTitle = "";
        public string TicketTitle { get => _ticketTitle; set { _ticketTitle = value; OnPropertyChanged(); } }

        private string _ticketDescription = "";
        public string TicketDescription { get => _ticketDescription; set { _ticketDescription = value; OnPropertyChanged(); } }

        private bool _includeDiagnostics = true;
        public bool IncludeDiagnostics { get => _includeDiagnostics; set { _includeDiagnostics = value; OnPropertyChanged(); } }

        private string _lastTicketResult = "—";
        public string LastTicketResult { get => _lastTicketResult; private set { _lastTicketResult = value; OnPropertyChanged(); } }

        private string _lastTicketId = "—";
        public string LastTicketId { get => _lastTicketId; private set { _lastTicketId = value; OnPropertyChanged(); } }

        private ObservableCollection<string> _recentTickets = new();
        public ObservableCollection<string> RecentTickets
        {
            get => _recentTickets;
            private set { _recentTickets = value; OnPropertyChanged(); }
        }

        // ---------------- Telemetry Settings ----------------

        private string _telemetryExePath = "";
        public string TelemetryExePath
        {
            get => _telemetryExePath;
            set
            {
                if (_telemetryExePath == value) return;
                _telemetryExePath = value;
                OnPropertyChanged();
                UpdateTelemetryStatus();
                RefreshSupportStatus();
            }
        }

        private bool _telemetryAutoStart;

        public bool AutoStartTelemetry
        {
            get => _telemetryAutoStart;
            set
            {
                if (_telemetryAutoStart == value) return;
                _telemetryAutoStart = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TelemetryAutoStart));
            }
        }

        public bool TelemetryAutoStart
        {
            get => AutoStartTelemetry;
            set => AutoStartTelemetry = value;
        }

        private string _telemetryStatusText = "";
        public string TelemetryStatusText
        {
            get => _telemetryStatusText;
            private set { _telemetryStatusText = value; OnPropertyChanged(); }
        }

        private string _telemetryStatusBrush = "#9FB0C7";
        public string TelemetryStatusBrush
        {
            get => _telemetryStatusBrush;
            private set { _telemetryStatusBrush = value; OnPropertyChanged(); }
        }

        private bool _telemetryIsRunning;
        public bool TelemetryIsRunning
        {
            get => _telemetryIsRunning;
            private set { _telemetryIsRunning = value; OnPropertyChanged(); }
        }

        // ---------------- Diagnostics ----------------

        private string _diagnosticsSummary = "";
        public string DiagnosticsSummary
        {
            get => _diagnosticsSummary;
            private set { _diagnosticsSummary = value; OnPropertyChanged(); }
        }

        // ---------------- Commands ----------------
        public ICommand OpenLogsFolderCommand { get; }
        public ICommand ExportDiagnosticsCommand { get; }
        public ICommand RefreshStatusCommand { get; }

        public ICommand OpenTelemetryDownloadCommand { get; }
        public ICommand BrowseTelemetryExeCommand { get; }
        public ICommand StartTelemetryNowCommand { get; }
        public ICommand SaveTelemetrySettingsCommand { get; }

        public ICommand CreateTicketCommand { get; }

        public ICommand OpenConnectVehicleCommand { get; }
        public ICommand OpenComplianceHelpCommand { get; }

        public SupportViewModel()
        {
            OpenLogsFolderCommand = new RelayCommand(() => Safe(SupportDiagnosticsService.OpenLogsFolder));
            ExportDiagnosticsCommand = new RelayCommand(() => Safe(ExportDiagnostics));
            RefreshStatusCommand = new RelayCommand(() => Safe(RefreshAll));

            OpenTelemetryDownloadCommand = new RelayCommand(() => Safe(OpenTelemetryDownload));
            BrowseTelemetryExeCommand = new RelayCommand(BrowseTelemetryExe);
            StartTelemetryNowCommand = new RelayCommand(() => Safe(StartTelemetryNow));
            SaveTelemetrySettingsCommand = new RelayCommand(() => Safe(SaveTelemetrySettingsFromView));

            CreateTicketCommand = new RelayCommand(async () => await SafeAsync(CreateTicketAsync));

            OpenConnectVehicleCommand = new RelayCommand(() => Safe(OpenConnectVehicle));
            OpenComplianceHelpCommand = new RelayCommand(() => Safe(OpenComplianceHelp));

            TelemetryExePath = ReadSetting("TelemetryExePath") ?? "";
            AutoStartTelemetry = ReadSettingBool("TelemetryAutoStart") ?? false;
            SenderEmail = ReadSetting("SupportSenderEmail") ?? "";

            LoadTicketHistory();
            RefreshAll();
        }

        private static void Safe(Action action)
        {
            try { action(); }
            catch { }
        }

        private static async Task SafeAsync(Func<Task> action)
        {
            try { await action().ConfigureAwait(true); }
            catch { }
        }

        private void RefreshAll()
        {
            TelemetryExePath = ReadSetting("TelemetryExePath") ?? TelemetryExePath;
            AutoStartTelemetry = ReadSettingBool("TelemetryAutoStart") ?? AutoStartTelemetry;
            SenderEmail = ReadSetting("SupportSenderEmail") ?? SenderEmail;

            UpdateTelemetryStatus();

            DiagnosticsSummary = BuildDiagnosticsSummary();
            RefreshSupportStatus();

            StatusLine = "Last updated: " + DateTime.Now.ToString("M/d h:mm:ss tt");
        }

        private void RefreshSupportStatus()
        {
            try
            {
                var app = System.Windows.Application.Current as App;
                if (app == null)
                {
                    StatusReport = "Support status unavailable.";
                    return;
                }

                var snap = app.Telemetry?.LastSnapshot;

                var streamOk = false;
                var streamAge = (double?)null;
                if (snap != null)
                {
                    streamAge = Math.Max(0, (DateTimeOffset.UtcNow - snap.SeenUtc).TotalSeconds);
                    streamOk = snap.Connected && streamAge.GetValueOrDefault(999) <= 5.0;
                }

                var cfg = VtcConfigService.Get();
                var hub = (cfg?.BotApiBaseUrl ?? "").Trim();

                var sb = new StringBuilder();
                sb.AppendLine("OverWatch ELD Support Status");
                sb.AppendLine($"Driver: {(app?.Session?.DriverName ?? EldDriverIdentityResolver.DriverName())}");
                sb.AppendLine($"Support email entered: {(string.IsNullOrWhiteSpace(SenderEmail) ? "No" : SenderEmail)}");
                sb.AppendLine($"Telemetry EXE linked: {(string.IsNullOrWhiteSpace(TelemetryExePath) ? "No" : "Yes")}");
                sb.AppendLine($"Telemetry server running: {(TelemetryIsRunning ? "Yes" : "No")}");
                sb.AppendLine($"ATS telemetry stream: {(streamOk ? "Connected ✅" : "Disconnected")}");
                if (streamAge.HasValue) sb.AppendLine($"Stream age: {streamAge.Value:0.0}s");
                sb.AppendLine($"Messaging hub: {(string.IsNullOrWhiteSpace(hub) ? "(not set)" : hub)}");
                sb.AppendLine($"Last ticket ID: {LastTicketId}");

                StatusReport = sb.ToString().TrimEnd();
            }
            catch
            {
                StatusReport = "Support status unavailable.";
            }
        }

        private string BuildDiagnosticsSummary()
        {
            try
            {
                var app = System.Windows.Application.Current as App;
                if (app == null)
                    return "";

                var snap = app.Telemetry?.LastSnapshot;
                var streamAge = snap == null ? (double?)null : Math.Max(0, (DateTimeOffset.UtcNow - snap.SeenUtc).TotalSeconds);

                var sb = new StringBuilder();
                sb.AppendLine("OverWatch ELD Diagnostics");
                sb.AppendLine($"User: {EldDriverIdentityResolver.DriverName()}");
                sb.AppendLine($"Machine: {Environment.MachineName}");
                sb.AppendLine($"OS: {Environment.OSVersion}");
                sb.AppendLine($"AppTime: {DateTimeOffset.Now:O}");
                sb.AppendLine($"TelemetryExePath: {(string.IsNullOrWhiteSpace(TelemetryExePath) ? "(not set)" : TelemetryExePath)}");
                sb.AppendLine($"TelemetryServerRunning: {TelemetryIsRunning}");
                sb.AppendLine($"TelemetryStreamConnected: {(snap?.Connected ?? false)}");
                sb.AppendLine($"TelemetryStreamAgeSec: {(streamAge.HasValue ? streamAge.Value.ToString("0.0", CultureInfo.InvariantCulture) : "(n/a)")}");
                sb.AppendLine($"TelemetryEndpoint: {(app?.Telemetry?.EndpointUrl ?? "(n/a)")}");
                sb.AppendLine($"Location: {(snap?.City ?? "")}, {(snap?.State ?? "")}".Trim().Trim(','));
                sb.AppendLine($"Truck: {(snap?.TruckMakeModel ?? "(n/a)")}");
                return sb.ToString().TrimEnd();
            }
            catch { return ""; }
        }

        private void UpdateTelemetryStatus()
        {
            try
            {
                TelemetryIsRunning = SupportDiagnosticsService.IsProcessRunning("ets2-telemetry-server")
                                    || SupportDiagnosticsService.IsProcessRunning("ats-telemetry-server")
                                    || SupportDiagnosticsService.IsProcessRunning("telemetry-server");

                var app = System.Windows.Application.Current as App;
                if (app == null)
                {
                    TelemetryStatusBrush = "#FF6B6B";
                    TelemetryStatusText = "Telemetry unavailable.";
                    return;
                }

                var snap = app.Telemetry?.LastSnapshot;
                var streamOk = false;
                if (snap != null)
                {
                    var age = Math.Max(0, (DateTimeOffset.UtcNow - snap.SeenUtc).TotalSeconds);
                    streamOk = snap.Connected && age <= 5.0;
                }

                if (string.IsNullOrWhiteSpace(TelemetryExePath))
                {
                    TelemetryStatusBrush = "#FF6B6B";
                    TelemetryStatusText = "Telemetry not linked. Click Browse and select the telemetry server .exe.";
                    return;
                }

                if (!File.Exists(TelemetryExePath))
                {
                    TelemetryStatusBrush = "#FFB020";
                    TelemetryStatusText = "Telemetry path is set, but file not found. Re-browse and select the correct .exe.";
                    return;
                }

                if (TelemetryIsRunning && streamOk)
                {
                    TelemetryStatusBrush = "#42D392";
                    TelemetryStatusText = "Telemetry linked + running ✅  |  ATS stream connected ✅";
                }
                else if (TelemetryIsRunning && !streamOk)
                {
                    TelemetryStatusBrush = "#57A6FF";
                    TelemetryStatusText = "Telemetry linked + running ✅  |  ATS stream: waiting… (start ATS / load a profile)";
                }
                else
                {
                    TelemetryStatusBrush = "#57A6FF";
                    TelemetryStatusText = "Telemetry linked ✅";
                }
            }
            catch
            {
                TelemetryStatusBrush = "#FF6B6B";
                TelemetryStatusText = "Telemetry status unavailable.";
            }
        }

        private void OpenTelemetryDownload()
        {
            SupportDiagnosticsService.OpenUrl("https://github.com/Funbit/ets2-telemetry-server/tags");
        }

        private void BrowseTelemetryExe()
        {
            MessageBox.Show(
                "Browse button clicked.",
                "OverWatch ELD",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Telemetry Server EXE",
                Filter = "EXE files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(TelemetryExePath))
            {
                var dir = Path.GetDirectoryName(TelemetryExePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    dialog.InitialDirectory = dir;
            }

            var owner = Application.Current?.Windows
    .OfType<Window>()
    .FirstOrDefault(w => w.IsActive)
    ?? Application.Current?.MainWindow;

            bool? result = owner != null
                ? dialog.ShowDialog(owner)
                : dialog.ShowDialog();

            if (result == true)
            {
                TelemetryExePath = dialog.FileName;
                SaveTelemetrySettingsFromView();

                MessageBox.Show(
                    "Telemetry file saved:\n\n" + TelemetryExePath,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void StartTelemetryNow()
        {
            var path = TelemetryExePath?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Telemetry EXE path is empty. Browse and select the telemetry server first.", "OverWatch ELD");
                return;
            }

            if (!File.Exists(path))
            {
                MessageBox.Show("Telemetry EXE was not found:\n\n" + path, "OverWatch ELD");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start telemetry reader:\n\n" + ex.Message, "OverWatch ELD");
            }

            UpdateTelemetryStatus();
            RefreshSupportStatus();
        }

        public void SaveTelemetrySettingsFromView()
        {
            try
            {
                WriteSetting("TelemetryExePath", TelemetryExePath ?? "");
                WriteSetting("TelemetryAutoStart", AutoStartTelemetry);
                WriteSetting("SupportSenderEmail", SenderEmail ?? "");

                UpdateTelemetryStatus();
                RefreshSupportStatus();

                LastTicketResult = "✅ Telemetry settings saved.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to save telemetry settings.\n\n" + ex.Message,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

       

        private void OpenConnectVehicle()
        {
            try
            {
                var app = System.Windows.Application.Current as App;

                if (app?.Telemetry == null)
                {
                    MessageBox.Show(
                        "Telemetry service not available.",
                        "OverWatch ELD",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                var w = new ConnectVehicleWindow(app.Telemetry);
                w.Owner = System.Windows.Application.Current?.MainWindow;
                w.ShowDialog();
            }
            catch { }
        }
        private void OpenComplianceHelp()
        {
            try
            {
                var w = new ReadMeWindow();
                w.Owner = Application.Current?.MainWindow;
                w.Show();
                w.Activate();
            }
            catch { }
        }

        private async Task CreateTicketAsync()
        {
            try
            {
                var title = (TicketTitle ?? "").Trim();
                var desc = (TicketDescription ?? "").Trim();
                var sender = (SenderEmail ?? "").Trim();

                if (string.IsNullOrWhiteSpace(sender))
                {
                    LastTicketResult = "❌ Enter your email first.";
                    return;
                }

                if (!LooksLikeEmail(sender))
                {
                    LastTicketResult = "❌ Enter a valid email address.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(desc))
                {
                    LastTicketResult = "❌ Enter a title or description first.";
                    return;
                }

                WriteSetting("SupportSenderEmail", sender);

                DiagnosticsSummary = BuildDiagnosticsSummary();

                var ticketId = GenerateTicketId();
                LastTicketId = ticketId;

                var subject = $"[OverWatchELD {ticketId}] {SelectedSeverity} - {SelectedCategory} - {(string.IsNullOrWhiteSpace(title) ? "Support Ticket" : title)}";

                var bodySb = new StringBuilder();
                bodySb.AppendLine("New OverWatch ELD Support Ticket");
                bodySb.AppendLine();
                bodySb.AppendLine($"Ticket ID: {ticketId}");
                bodySb.AppendLine($"From: {sender}");
                bodySb.AppendLine($"Category: {SelectedCategory}");
                bodySb.AppendLine($"Severity: {SelectedSeverity}");
                bodySb.AppendLine($"Title: {(string.IsNullOrWhiteSpace(title) ? "(none)" : title)}");
                bodySb.AppendLine($"TimeLocal: {DateTimeOffset.Now:O}");
                bodySb.AppendLine($"Machine: {Environment.MachineName}");
                bodySb.AppendLine();

                bodySb.AppendLine("Description:");
                bodySb.AppendLine(string.IsNullOrWhiteSpace(desc) ? "(none)" : desc);

                if (IncludeDiagnostics)
                {
                    bodySb.AppendLine();
                    bodySb.AppendLine("--- Diagnostics Summary ---");
                    bodySb.AppendLine(DiagnosticsSummary ?? "");
                }

                string diagnosticsAttachmentPath = "";
                if (IncludeDiagnostics)
                    diagnosticsAttachmentPath = CreateDiagnosticsAttachment(ticketId);

                await SendSupportEmailAsync(
                    subject: subject,
                    body: bodySb.ToString(),
                    senderEmail: sender,
                    diagnosticsAttachmentPath: diagnosticsAttachmentPath);

                LastTicketResult = $"✅ Ticket sent successfully. Ticket ID: {ticketId}";
                SaveTicketHistory(new SupportTicketRecord
                {
                    TicketId = ticketId,
                    SenderEmail = sender,
                    Category = SelectedCategory,
                    Severity = SelectedSeverity,
                    Title = title,
                    Description = desc,
                    IncludeDiagnostics = IncludeDiagnostics,
                    DiagnosticsPath = diagnosticsAttachmentPath,
                    CreatedLocal = DateTime.Now.ToString("O"),
                    Result = "Sent"
                });

                TicketTitle = "";
                TicketDescription = "";
                OnPropertyChanged(nameof(TicketTitle));
                OnPropertyChanged(nameof(TicketDescription));

                LoadTicketHistory();
                RefreshSupportStatus();
            }
            catch (Exception exOuter)
            {
                LastTicketResult = "❌ Ticket send failed: " + exOuter.Message;

                try
                {
                    SaveTicketHistory(new SupportTicketRecord
                    {
                        TicketId = LastTicketId == "—" ? GenerateTicketId() : LastTicketId,
                        SenderEmail = SenderEmail ?? "",
                        Category = SelectedCategory ?? "",
                        Severity = SelectedSeverity ?? "",
                        Title = TicketTitle ?? "",
                        Description = TicketDescription ?? "",
                        IncludeDiagnostics = IncludeDiagnostics,
                        DiagnosticsPath = "",
                        CreatedLocal = DateTime.Now.ToString("O"),
                        Result = "Failed: " + exOuter.Message
                    });
                    LoadTicketHistory();
                }
                catch { }
            }
        }

        private async Task SendSupportEmailAsync(string subject, string body, string senderEmail, string diagnosticsAttachmentPath)
        {
            var smtpPassword = Environment.GetEnvironmentVariable(SmtpPasswordEnvVar);
            if (string.IsNullOrWhiteSpace(smtpPassword))
                throw new Exception($"SMTP password not configured. Set environment variable {SmtpPasswordEnvVar}.");

            using var smtp = new SmtpClient(SmtpHost, SmtpPort)
            {
                Credentials = new NetworkCredential(TicketEmail, smtpPassword),
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000
            };

            using var mail = new MailMessage
            {
                From = new MailAddress(TicketEmail, "OverWatch ELD Support"),
                Subject = subject,
                Body = body,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };

            mail.To.Add(TicketEmail);
            mail.ReplyToList.Add(new MailAddress(senderEmail));

            if (!string.IsNullOrWhiteSpace(diagnosticsAttachmentPath) && File.Exists(diagnosticsAttachmentPath))
            {
                var attachment = new Attachment(diagnosticsAttachmentPath);
                mail.Attachments.Add(attachment);
            }

            await smtp.SendMailAsync(mail).ConfigureAwait(true);
        }

        private string CreateDiagnosticsAttachment(string ticketId)
        {
            try
            {
                Directory.CreateDirectory(DiagnosticsDir);
                var path = Path.Combine(DiagnosticsDir, $"OverWatchELD_Diagnostics_{ticketId}.txt");
                File.WriteAllText(path, DiagnosticsSummary ?? "", Encoding.UTF8);
                return path;
            }
            catch
            {
                return "";
            }
        }

        private static bool LooksLikeEmail(string value)
        {
            try
            {
                var addr = new MailAddress(value);
                return string.Equals(addr.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string GenerateTicketId()
        {
            return "OW-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }

        private void LoadTicketHistory()
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);

                if (!File.Exists(TicketHistoryPath))
                {
                    RecentTickets = new ObservableCollection<string>();
                    return;
                }

                var json = File.ReadAllText(TicketHistoryPath);
                var items = JsonSerializer.Deserialize<List<SupportTicketRecord>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<SupportTicketRecord>();

                var lines = items
                    .OrderByDescending(x => x.CreatedLocal)
                    .Take(10)
                    .Select(x => $"{x.TicketId} | {x.Severity} | {x.Category} | {x.Result}")
                    .ToList();

                RecentTickets = new ObservableCollection<string>(lines);
            }
            catch
            {
                RecentTickets = new ObservableCollection<string>();
            }
        }

        private void SaveTicketHistory(SupportTicketRecord record)
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);

                List<SupportTicketRecord> items;
                if (File.Exists(TicketHistoryPath))
                {
                    var jsonOld = File.ReadAllText(TicketHistoryPath);
                    items = JsonSerializer.Deserialize<List<SupportTicketRecord>>(jsonOld, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<SupportTicketRecord>();
                }
                else
                {
                    items = new List<SupportTicketRecord>();
                }

                items.Add(record);

                var jsonNew = JsonSerializer.Serialize(items, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(TicketHistoryPath, jsonNew, Encoding.UTF8);
            }
            catch { }
        }

        private void ExportDiagnostics()
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var path = Path.Combine(desktop, $"OverWatchELD_Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, DiagnosticsSummary ?? "", Encoding.UTF8);
                SupportDiagnosticsService.OpenFileInExplorer(path);
            }
            catch { }
        }

        // ---------------- settings helpers ----------------
        private static string? ReadSetting(string key)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                var json = File.ReadAllText(SettingsPath);
                if (string.IsNullOrWhiteSpace(json)) return null;
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(key, out var p)) return null;
                return p.ValueKind == JsonValueKind.String ? (p.GetString() ?? "") : p.ToString();
            }
            catch { return null; }
        }

        private static bool? ReadSettingBool(string key)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                var json = File.ReadAllText(SettingsPath);
                if (string.IsNullOrWhiteSpace(json)) return null;
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(key, out var p)) return null;
                return p.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => p.TryGetInt32(out var i) && i != 0,
                    JsonValueKind.String => bool.TryParse(p.GetString(), out var b) ? b : (bool?)null,
                    _ => null
                };
            }
            catch { return null; }
        }

        private static void WriteSetting(string key, object? value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath) ?? Environment.CurrentDirectory);

                JsonElement? existing = null;
                if (File.Exists(SettingsPath))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                        existing = doc.RootElement.Clone();
                    }
                    catch { existing = null; }
                }

                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();

                    if (existing.HasValue && existing.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in existing.Value.EnumerateObject())
                        {
                            if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                                continue;
                            prop.WriteTo(writer);
                        }
                    }

                    writer.WritePropertyName(key);
                    if (value == null)
                    {
                        writer.WriteNullValue();
                    }
                    else if (value is bool b)
                    {
                        writer.WriteBooleanValue(b);
                    }
                    else
                    {
                        writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                    }

                    writer.WriteEndObject();
                }

                File.WriteAllBytes(SettingsPath, ms.ToArray());
            }
            catch { }
        }
    }
}