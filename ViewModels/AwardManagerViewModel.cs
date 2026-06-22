using OverWatchELD.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace OverWatchELD.ViewModels
{
    public sealed class AwardManagerViewModel : INotifyPropertyChanged
    {
        private readonly VtcAwardsApiService _api = new VtcAwardsApiService();

        private string _awardName = "";
        private string _awardDescription = "";
        private string _awardEmoji = "🏆";
        private string _note = "";
        private bool _isAchievement;
        private VtcAwardsApiService.VtcAwardDto? _selectedAward;

        public ObservableCollection<VtcAwardsApiService.VtcAwardDto> Awards { get; } = new();

        public string AwardName
        {
            get => _awardName;
            set
            {
                if (_awardName == value) return;
                _awardName = value;
                OnPropertyChanged();
            }
        }

        public string AwardDescription
        {
            get => _awardDescription;
            set
            {
                if (_awardDescription == value) return;
                _awardDescription = value;
                OnPropertyChanged();
            }
        }

        public string AwardEmoji
        {
            get => _awardEmoji;
            set
            {
                if (_awardEmoji == value) return;
                _awardEmoji = value;
                OnPropertyChanged();
            }
        }

        public string Note
        {
            get => _note;
            set
            {
                if (_note == value) return;
                _note = value;
                OnPropertyChanged();
            }
        }

        public bool IsAchievement
        {
            get => _isAchievement;
            set
            {
                if (_isAchievement == value) return;
                _isAchievement = value;
                OnPropertyChanged();
            }
        }

        public VtcAwardsApiService.VtcAwardDto? SelectedAward
        {
            get => _selectedAward;
            set
            {
                if (_selectedAward == value) return;
                _selectedAward = value;
                OnPropertyChanged();
            }
        }

        public async Task LoadAwardsAsync(string botBaseUrl, string guildId)
        {
            Awards.Clear();

            var items = await _api.GetAwardsAsync(botBaseUrl, guildId);
            foreach (var item in items)
                Awards.Add(item);
        }

        public async Task<VtcAwardsApiService.VtcAwardDto?> CreateAwardAsync(
            string botBaseUrl,
            VtcAwardsApiService.CreateAwardReq req)
        {
            var created = await _api.CreateAwardAsync(botBaseUrl, req);
            if (created != null)
            {
                Awards.Add(created);
                SelectedAward = created;
            }

            return created;
        }

        public Task<bool> AssignAwardAsync(string botBaseUrl, VtcAwardsApiService.AssignAwardReq req)
        {
            return _api.AssignAwardAsync(botBaseUrl, req);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}