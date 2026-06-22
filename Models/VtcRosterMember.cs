using System.Collections.ObjectModel;

namespace OverWatchELD.Models
{
    public class VtcRosterMember
    {
        public string DriverId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DiscordName { get; set; } = "";
        public string Rank { get; set; } = "Driver";

        public double TotalDistanceMiles { get; set; }
        public double TotalMassLbs { get; set; }

        public ObservableCollection<string> Achievements { get; set; } = new();

        public bool IsOwner { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }

        public string Initials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(DisplayName))
                    return "?";

                var parts = DisplayName.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                    return parts[0].Substring(0, 1).ToUpper();

                return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
            }
        }

        public string AchievementsSummary
        {
            get
            {
                if (Achievements == null || Achievements.Count == 0)
                    return "None";

                return string.Join(" • ", Achievements);
            }
        }
    }
}