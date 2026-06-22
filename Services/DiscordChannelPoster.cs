using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    /// <summary>
    /// DISABLED: ELD -> Discord posting has been removed per project direction.
    ///
    /// We keep this stub so any older code that still calls it will compile,
    /// but it will never send anything to Discord.
    /// </summary>
    public static class DiscordChannelPoster
    {
        public static Task<(bool ok, string error)> PostMessageAsync(string botToken, string channelId, string content)
            => Task.FromResult((false, "ELD->Discord announcements disabled"));
    }
}
