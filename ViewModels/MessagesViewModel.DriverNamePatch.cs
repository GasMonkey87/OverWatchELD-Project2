using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public partial class MessagesViewModel
    {
        public string DisplayName => UserSession.Instance.DisplayName;
        public string UserName => UserSession.Instance.DisplayName;

        public void OverWatchRefreshDisplayName()
        {
            try
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(UserName));
            }
            catch { }
        }
    }
}