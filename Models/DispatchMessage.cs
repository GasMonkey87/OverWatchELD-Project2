using System.Text.Json.Serialization;
using System;

namespace OverWatchELD.Models
{
   
    public sealed class DispatchMessage
    {
        // ---------------- Canonical fields ----------------
        public string? MsgId { get; set; } = Guid.NewGuid().ToString("N");
        [JsonPropertyName("discordUserId")]
        public string? DiscordUserId { get; set; } = "";
        public DateTimeOffset DateUtc { get; set; } = DateTimeOffset.UtcNow;

        [System.Obsolete("Use DiscordUserId instead.")]
        public string? DriverId { get => DiscordUserId; set => DiscordUserId = value; }

        public string? Priority { get; set; } = "Normal";
        public string? LoadNumber { get; set; }
        public bool RequiresAck { get; set; }

        public string? From { get; set; }
        public string? Title { get; set; }
        public string? Text { get; set; }

        // Reply from driver
        public string? ReplyText { get; set; }
        public DateTimeOffset? ReplyDateUtc { get; set; }

        // ---------------- Compatibility aliases ----------------
        // Many services in this repo were written against a slightly different
        // DispatchMessage shape. Provide aliases so we can compile without
        // rewriting UI/logic.
        public string Id
        {
            get => MsgId ?? "";
            set => MsgId = value;
        }

        public string DriverKey
        {
            get => DiscordUserId ?? "";
            set => DiscordUserId = value;
        }

        public DateTimeOffset SentUtc
        {
            get => DateUtc;
            set => DateUtc = value;
        }

        public string? LoadId
        {
            get => LoadNumber;
            set => LoadNumber = value;
        }

        public string? LastReplyText
        {
            get => ReplyText;
            set => ReplyText = value;
        }

        public DateTimeOffset? LastReplyUtc
        {
            get => ReplyDateUtc;
            set => ReplyDateUtc = value;
        }

        public bool IsRead { get; set; }
        public DispatchDecision Decision { get; set; } = DispatchDecision.None;
        public DateTimeOffset? DecisionUtc { get; set; }
    }
}
