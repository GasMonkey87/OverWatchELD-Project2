using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OverWatchELD.Models
{
    public sealed class InspectionChecklistItem : INotifyPropertyChanged
    {
        private bool _isOk = true;
        private bool _isDefect;
        private string _note = string.Empty;

        public string Category { get; }
        public string Name { get; }
        public string Key { get; }

        public InspectionChecklistItem(string category, string name, string key)
        {
            Category = category;
            Name = name;
            Key = key;
        }

        public string Display => $"{Category}: {Name}";

        public bool IsOk
        {
            get => _isOk;
            set
            {
                if (_isOk == value) return;
                _isOk = value;
                if (value) IsDefect = false;
                OnPropertyChanged();
            }
        }

        public bool IsDefect
        {
            get => _isDefect;
            set
            {
                if (_isDefect == value) return;
                _isDefect = value;
                if (value) _isOk = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOk));
            }
        }

        public string Note
        {
            get => _note;
            set
            {
                if (_note == value) return;
                _note = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
