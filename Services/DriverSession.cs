using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OverWatchELD.Services
{
    public sealed class DriverSession : INotifyPropertyChanged
    {
        private string _driverName = "Driver";

        public string DriverName
        {
            get => _driverName;
            set
            {
                if (_driverName != value)
                {
                    _driverName = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
