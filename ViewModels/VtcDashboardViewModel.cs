using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OverWatchELD.ViewModels
{
    public sealed class VtcDashboardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string Title => "VTC Dashboard";
        public string Subtitle => "Your VTC connection and tools live here.";
    }
}