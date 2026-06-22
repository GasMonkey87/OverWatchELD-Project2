using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    /// <summary>
    /// Compatibility shim for older toolbar patches that expect a "clocks VM".
    /// The DashboardView itself binds to DashboardViewModel.*Time strings.
    /// </summary>
    public sealed class DashboardClocksLiveViewModel : INotifyPropertyChanged
    {
        public static DashboardClocksLiveViewModel Shared { get; } = new DashboardClocksLiveViewModel();

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _notes = "";
        public string Notes
        {
            get => _notes;
            set { if (_notes != value) { _notes = value; OnPropertyChanged(); } }
        }

        private bool _pulse;
        public bool Pulse
        {
            get => _pulse;
            set { if (_pulse != value) { _pulse = value; OnPropertyChanged(); } }
        }

        private DashboardClocksLiveViewModel() { }

        /// <summary>
        /// Manual reset is handled by HosCalculator; this keeps the existing button wired.
        /// </summary>
        public void ManualResetAllClocks()
        {
            try
            {
                HosCalculator.ManualResetAllClocks();

                Notes = "Clocks manually reset.";
                Pulse = true;

                RefreshNow();
            }
            catch (Exception ex)
            {
                Notes = $"Reset failed: {ex.Message}";
                Pulse = true;
            }
        }

        public void RefreshNow()
        {
            try
            {
                OnPropertyChanged(nameof(Notes));
                OnPropertyChanged(nameof(Pulse));
            }
            catch
            {
                // ignore
            }
        }
    }
}