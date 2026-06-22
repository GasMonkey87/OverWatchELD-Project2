using OverWatchELD.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OverWatchELD.ViewModels
{
    public abstract class DisplayNameViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string DisplayName => UserSession.Instance.DisplayName;

        /// <summary>
        /// Call this when login name changes to refresh bindings.
        /// </summary>
        public void RefreshDisplayName()
        {
            OnPropertyChanged(nameof(DisplayName));
        }
    }
}
