namespace OverWatchELD.Models.Vtc
{
    public sealed class AssignAwardReq
    {
        public string GuildId { get; set; } = "";
        public string DriverId { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string AwardId { get; set; } = "";
        public string AwardedByUserId { get; set; } = "";
        public string AwardedByUsername { get; set; } = "";
        public string Note { get; set; } = "";
    }
}