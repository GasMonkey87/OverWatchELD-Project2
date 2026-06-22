namespace OverWatchELD.Services
{
    public sealed class UserSession
    {
        private static readonly UserSession _instance = new UserSession();
        public static UserSession Instance => _instance;

        private UserSession() { }

        public string DisplayName { get; private set; } = "User";

        public void SetDisplayName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                DisplayName = name.Trim();
        }
    }
}
