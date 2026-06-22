using System;

namespace OverWatchELD.Models.Media
{
    public sealed class MediaTrack
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string Source { get; set; } = "Local ATS Music";
        public TimeSpan Duration { get; set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Artist))
                    return $"{Title} — {Artist}";

                return string.IsNullOrWhiteSpace(Title) ? FullPath : Title;
            }
        }
    }
}
