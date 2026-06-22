using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OverWatchELD.Models
{
    public class RingGaugeModel : INotifyPropertyChanged
    {
        private string _title = "";
        private string _timeText = "00:00";
        private string _subtitle = "";
        private double _progress; // 0..1

        public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
        public string TimeText { get => _timeText; set { _timeText = value; OnPropertyChanged(); } }
        public string Subtitle { get => _subtitle; set { _subtitle = value; OnPropertyChanged(); } }
        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
