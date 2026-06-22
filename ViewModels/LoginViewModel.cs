using System;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public sealed class LoginViewModel : INotifyPropertyChanged
    {
        private readonly Window _window;

        private string _driverName = "Driver";
        private string _provider = "None";
        private string _vtcName = "";
        private string _discordBotLinkCode = "";
        private string _statusText = "Not connected.";

        // Discord bot link state lives here (since AppSession doesn't have fields)
        private string _botApiKey = "";
        private string _botBaseUrl = "";

        // ✅ change this when you move bot to VPS
        private string _linkSecret = "change_me";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string DriverName
        {
            get => _driverName;
            set { if (_driverName != value) { _driverName = value; OnPropertyChanged(); } }
        }

        public string Provider
        {
            get => _provider;
            set
            {
                if (_provider == value) return;
                _provider = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDiscordBot));
            }
        }

        public bool IsDiscordBot => string.Equals(Provider, "Discord Bot", StringComparison.OrdinalIgnoreCase);

        public string VtcName
        {
            get => _vtcName;
            set { if (_vtcName != value) { _vtcName = value; OnPropertyChanged(); } }
        }

        public string DiscordBotLinkCode
        {
            get => _discordBotLinkCode;
            set { if (_discordBotLinkCode != value) { _discordBotLinkCode = value; OnPropertyChanged(); } }
        }

        public string StatusText
        {
            get => _statusText;
            set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
        }

        public ICommand LinkDiscordBotCommand { get; }
        public ICommand ContinueCommand { get; }
        public ICommand SkipCommand { get; }

        public LoginViewModel(Window window)
        {
            _window = window;

            LinkDiscordBotCommand = new RelayCommand(async _ => await LinkDiscordBotAsync());
            ContinueCommand = new RelayCommand(async _ => await ContinueAsync());
            SkipCommand = new RelayCommand(_ => Skip());

                        Provider = "None";

            // Load VTC/Discord config (user-editable file next to the EXE)
            try
            {
                var cfg = VtcConfigService.Load();
                _botBaseUrl = (cfg.BotApiBaseUrl ?? _botBaseUrl).Trim();
                _linkSecret = (cfg.BotLinkSecret ?? _linkSecret).Trim();
                if (string.IsNullOrWhiteSpace(VtcName))
                    VtcName = (cfg.VtcName ?? "").Trim();
            }
            catch { }
}

        private OverWatchELD.App? GetApp() => Application.Current as OverWatchELD.App;

        private static string SafeTrim(string? s) => (s ?? "").Trim();

        private AppSession BuildBaseSession()
        {
            // Only set members that exist on AppSession (per your build errors)
            return new AppSession
            {
                DriverName = SafeTrim(DriverName),
                VtcProvider = SafeTrim(Provider)
            };
        }

        private async Task LinkDiscordBotAsync()
        {
            if (!IsDiscordBot)
            {
                StatusText = "Set Provider to 'Discord Bot' first.";
                return;
            }

            if (string.IsNullOrWhiteSpace(DiscordBotLinkCode))
            {
                StatusText = "Missing link code. In Discord run: /eld connect";
                return;
            }

            StatusText = "Linking…";

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                var api = new DiscordBotApiClient(http);

                var (ok, apiKey, msg) = await api.ConfirmLinkAsync(
                    _botBaseUrl,
                    _linkSecret,
                    DiscordBotLinkCode.Trim(),
                    DriverName.Trim(),
                    Environment.MachineName);

                if (!ok || string.IsNullOrWhiteSpace(apiKey))
                {
                    StatusText = msg;
                    _botApiKey = "";
                    return;
                }

                _botApiKey = apiKey;

                // Store minimal session to App
                var app = GetApp();
                if (app != null)
                {
                    var session = BuildBaseSession();
                    session.VtcProvider = "Discord Bot";
                    app.SetSession(session);
                }

                StatusText = "Linked ✅ (Continue)";
            }
            catch (Exception ex)
            {
                _botApiKey = "";
                StatusText = "Link error: " + ex.Message;
            }
        }

        private async Task ContinueAsync()
        {
            if (string.IsNullOrWhiteSpace(DriverName))
            {
                MessageBox.Show("Please enter a driver name.", "Login", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var app = GetApp();

            if (IsDiscordBot)
            {
                // Require link before continuing (use VM-held api key)
                if (string.IsNullOrWhiteSpace(_botApiKey))
                {
                    await LinkDiscordBotAsync();
                    if (string.IsNullOrWhiteSpace(_botApiKey))
                        return;
                }

                // Ensure App session is set
                if (app != null)
                {
                    var session = BuildBaseSession();
                    session.VtcProvider = "Discord Bot";
                    app.SetSession(session);
                }
            }
            else
            {
                // Offline/other provider
                if (app != null)
                {
                    var session = BuildBaseSession();
                    session.VtcProvider = SafeTrim(Provider);
                    app.SetSession(session);
                }
            }

            try { app?.TryAutoStartTelemetry(); } catch { }

            OpenMainWindow();
        }

        private void Skip()
        {
            if (string.IsNullOrWhiteSpace(DriverName))
                DriverName = "Driver";

            _botApiKey = "";

            var app = GetApp();
            if (app != null)
            {
                var session = BuildBaseSession();
                session.VtcProvider = "None";
                app.SetSession(session);
            }

            try { app?.TryAutoStartTelemetry(); } catch { }

            OpenMainWindow();
        }

        private void OpenMainWindow()
        {
            try
            {
                var mw = new MainWindow();
                Application.Current.MainWindow = mw;
                mw.Show();
                mw.Activate();
                _window.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Open MainWindow Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;
        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);

        public sealed class DiscordGuildOption
        {
            public ulong Id { get; init; }
            public string Name { get; init; } = "";
        }

        public ObservableCollection<DiscordGuildOption> DiscordGuilds { get; } = new();
        private DiscordGuildOption? _selectedDiscordGuild;
        public DiscordGuildOption? SelectedDiscordGuild
        {
            get => _selectedDiscordGuild;
            set
            {
                _selectedDiscordGuild = value;
                OnPropertyChanged();
                // Save selection to your settings/db here if you want it remembered
            }
        }

        private bool _isDiscordConnected;
        public bool IsDiscordConnected
        {
            get => _isDiscordConnected;
            set { _isDiscordConnected = value; OnPropertyChanged(); }
        }
    }
}
