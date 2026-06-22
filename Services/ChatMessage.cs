using System;
using System.Collections.Generic;

namespace OverWatchELD.Models
{
    public class ChatMessage
    {
        public string Id { get; set; } = "";
        public string ChannelId { get; set; } = "";
        public string From { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; }
        public List<string> Attachments { get; set; } = new();

        // Local UI
        public bool IsSelected { get; set; }
    }
}