using System.Text.Json.Serialization;

namespace OverWatchELD.VtcBot.Models;

public sealed class SendMessageReq
{
    // REQUIRED
    public string? GuildId { get; set; }

    // Sender
    public string? DriverName { get; set; }

    // Target (optional = dispatch)
    public string? TargetDiscordUserId { get; set; }

    // Message content
    public string? Message { get; set; }

    // Optional load info
    public string? LoadNumber { get; set; }
}