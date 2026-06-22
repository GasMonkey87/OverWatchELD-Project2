using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public partial class DashboardViewModel
    {
        // Universal name source
        public string DisplayName => UserSession.Instance.DisplayName;

        // Common greeting property names (so your existing XAML picks it up)
        public string Greeting => $"Welcome, {UserSession.Instance.DisplayName}";
        public string GreetingText => $"Welcome, {UserSession.Instance.DisplayName}";
        public string WelcomeText => $"Welcome, {UserSession.Instance.DisplayName}";

        // Call this after login (or on dashboard loaded) to refresh bindings
        public void OverWatchRefreshDisplayName()
        {
            try
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Greeting));
                OnPropertyChanged(nameof(GreetingText));
                OnPropertyChanged(nameof(WelcomeText));
            }
            catch { }
        }

        // Back-compat: some code-behind calls this older name.
        public void OverWatchRefreshDriverName() => OverWatchRefreshDisplayName();
    }
}
