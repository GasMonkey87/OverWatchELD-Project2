using System;
using System.Windows.Threading;

namespace OverWatchELD.Services
{
    public enum DashboardToastType
    {
        Info,
        Success,
        Warning,
        Danger,
        Message,
        Malfunction
    }

    public sealed class DashboardToastEventArgs : EventArgs
    {
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public DashboardToastType Type { get; set; } = DashboardToastType.Info;
    }

    public static class DashboardToastService
    {
        public static event EventHandler<DashboardToastEventArgs>? ToastRequested;

        public static void Show(string title, string message, DashboardToastType type = DashboardToastType.Info)
        {
            var args = new DashboardToastEventArgs
            {
                Title = title ?? "",
                Message = message ?? "",
                Type = type
            };

            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;

                if (dispatcher == null || dispatcher.CheckAccess())
                    ToastRequested?.Invoke(null, args);
                else
                    dispatcher.BeginInvoke(() => ToastRequested?.Invoke(null, args));
            }
            catch
            {
            }
        }

        public static void Message(string from, string text)
        {
            Show(
                string.IsNullOrWhiteSpace(from) ? "New Message" : $"Message from {from}",
                text,
                DashboardToastType.Message);
        }

        public static void Malfunction(string truck, string issue)
        {
            Show(
                "Vehicle Malfunction",
                $"{truck}: {issue}",
                DashboardToastType.Malfunction);
        }

        public static void Alert(string title, string message)
        {
            Show(title, message, DashboardToastType.Warning);
        }
    }
}