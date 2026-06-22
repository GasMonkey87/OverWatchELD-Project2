using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Session state used by UI (Dashboard, profiles, etc).
    /// Must support DriverName, PropertyChanged subscriptions, and profile selection.
    /// </summary>
    public class SessionService : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _driverName = "Driver";
        private string _carrierName = "";
        private string _truckNumber = "";
        private string _trailerNumber = "";

        private object? _selectedProfile;
        private string _profileName = "";

        public string DriverName
        {
            get => _driverName;
            set
            {
                if (_driverName == value) return;
                _driverName = value ?? "Driver";
                OnPropertyChanged();
            }
        }

        public string CarrierName
        {
            get => _carrierName;
            set
            {
                if (_carrierName == value) return;
                _carrierName = value ?? "";
                OnPropertyChanged();
            }
        }

        public string TruckNumber
        {
            get => _truckNumber;
            set
            {
                if (_truckNumber == value) return;
                _truckNumber = value ?? "";
                OnPropertyChanged();
            }
        }

        public string TrailerNumber
        {
            get => _trailerNumber;
            set
            {
                if (_trailerNumber == value) return;
                _trailerNumber = value ?? "";
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Holds whatever profile object your ProfileSelectViewModel passes in.
        /// Kept as object to avoid tight coupling to a specific Profile model type.
        /// </summary>
        public object? SelectedProfile
        {
            get => _selectedProfile;
            private set
            {
                if (ReferenceEquals(_selectedProfile, value)) return;
                _selectedProfile = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// A friendly name for the selected profile (optional).
        /// If your profile object has a Name property, SetProfile will try to read it.
        /// </summary>
        public string ProfileName
        {
            get => _profileName;
            private set
            {
                if (_profileName == value) return;
                _profileName = value ?? "";
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Called by ProfileSelectViewModel to apply a chosen profile to the current session.
        /// Signature kept broad to match likely call sites without needing model references.
        /// </summary>
        public void SetProfile(object? profile)
        {
            SelectedProfile = profile;

            // Best-effort: read a "Name" property if the profile has it.
            // This avoids hard dependency on a specific Profile class.
            ProfileName = TryGetStringProperty(profile, "Name") ?? TryGetStringProperty(profile, "ProfileName") ?? "";

            // If your profile includes these common fields, grab them too (optional).
            var dn = TryGetStringProperty(profile, "DriverName");
            if (!string.IsNullOrWhiteSpace(dn)) DriverName = dn!;

            var cn = TryGetStringProperty(profile, "CarrierName");
            if (!string.IsNullOrWhiteSpace(cn)) CarrierName = cn!;

            var tn = TryGetStringProperty(profile, "TruckNumber");
            if (!string.IsNullOrWhiteSpace(tn)) TruckNumber = tn!;

            var trn = TryGetStringProperty(profile, "TrailerNumber");
            if (!string.IsNullOrWhiteSpace(trn)) TrailerNumber = trn!;
        }

        private static string? TryGetStringProperty(object? obj, string propName)
        {
            if (obj == null) return null;
            try
            {
                var t = obj.GetType();
                var p = t.GetProperty(propName);
                if (p == null) return null;
                var v = p.GetValue(obj);
                return v?.ToString();
            }
            catch
            {
                return null;
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
