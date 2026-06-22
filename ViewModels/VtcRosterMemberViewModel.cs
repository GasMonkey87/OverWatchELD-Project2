using OverWatchELD.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OverWatchELD.ViewModels
{
    public class VtcRosterMemberViewModel : INotifyPropertyChanged
    {
        public VtcRosterMember Model { get; }

        public VtcRosterMemberViewModel(VtcRosterMember model, bool currentUserCanManageRoster)
        {
            Model = model;
            _currentUserCanManageRoster = currentUserCanManageRoster;
        }

        private readonly bool _currentUserCanManageRoster;

        public string DriverId => Model.DriverId;
        public string DisplayName => Model.DisplayName;
        public string DiscordName => Model.DiscordName;
        public string Rank => Model.Rank;
        public double TotalDistanceMiles => Model.TotalDistanceMiles;
        public double TotalMassLbs => Model.TotalMassLbs;
        public string Initials => Model.Initials;
        public string AchievementsSummary => Model.AchievementsSummary;

        public bool IsOwner => Model.IsOwner;
        public bool IsAdmin => Model.IsAdmin;
        public bool IsManager => Model.IsManager;

        public bool CanManageRoster => _currentUserCanManageRoster;

        public string DistanceText => $"{TotalDistanceMiles:N0} mi";
        public string MassText => $"{TotalMassLbs:N0} lbs";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}