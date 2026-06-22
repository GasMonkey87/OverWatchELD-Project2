using OverWatchELD.Services;
using OverWatchELD.Views;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace OverWatchELD
{
    public partial class App : Application
    {
        public dynamic Session { get; private set; } = new SafeDynamicBag();
        public dynamic SessionState { get; set; } = new SafeDynamicBag();

        public TelemetryService Telemetry { get; private set; } = new TelemetryService();
        public DutyStateMachine DutyMachine { get; private set; } = new DutyStateMachine();

        public dynamic? BotApi { get; private set; }

        private bool _dashboardServicesStarted;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                EnsureSession();
                InitializeCoreServices();
                ForceReloadVtcConfigSafe();

                var login = new LoginWindow();
                MainWindow = login;
                login.Show();

                StartLightBackgroundServicesAfterUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Startup error:\n" + ex.Message, "OverWatch ELD");
                Shutdown();
            }
        }

        private void StartLightBackgroundServicesAfterUi()
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1200);
                    TryAutoStartTelemetry();
                });
            }), DispatcherPriority.Background);
        }

        public void StartDashboardBackgroundServices()
        {
            if (_dashboardServicesStarted)
                return;

            _dashboardServicesStarted = true;

            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500);

                    try
                    {
                        // Starts ATS mod scanner after dashboard is already usable.
                        AtsModBackgroundLoadService.StartOnce();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[APP] ATS mod background load failed: " + ex.Message);
                    }
                });
            }), DispatcherPriority.Background);
        }

        public void EnsureSession()
        {
            Session ??= new SafeDynamicBag();
            SessionState ??= new SafeDynamicBag();
        }

        public void ForceReloadVtcConfigSafe()
        {
            try { VtcConfigService.Load(); } catch { }
        }

        public void TryAutoStartTelemetry()
        {
            try
            {
                Telemetry.Start();
                System.Diagnostics.Debug.WriteLine("[APP] TelemetryService.Start() called.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[APP] Telemetry start failed: " + ex.Message);
            }
        }

        private void InitializeCoreServices()
        {
            Telemetry ??= new TelemetryService();
            DutyMachine ??= new DutyStateMachine();

            BotApi ??= CreateService(
                "OverWatchELD.Services.BotApi",
                "OverWatchELD.Services.BotApiService",
                "OverWatchELD.Services.BotApiClient",
                "OverWatchELD.Services.VtcBotApiService"
            );
        }

        private static object? CreateService(params string[] typeNames)
        {
            foreach (var typeName in typeNames)
            {
                try
                {
                    var type = Type.GetType(typeName);
                    if (type == null) continue;

                    var ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor == null) continue;

                    return Activator.CreateInstance(type);
                }
                catch { }
            }

            return new SafeDynamicBag();
        }

        private sealed class SafeDynamicBag : DynamicObject
        {
            private readonly Dictionary<string, object?> _values = new();

            public override bool TryGetMember(GetMemberBinder binder, out object? result)
            {
                if (_values.TryGetValue(binder.Name, out result))
                    return true;

                result = null;
                return true;
            }

            public override bool TrySetMember(SetMemberBinder binder, object? value)
            {
                _values[binder.Name] = value;
                return true;
            }

            public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
            {
                result = null;
                return true;
            }
        }
    }
}