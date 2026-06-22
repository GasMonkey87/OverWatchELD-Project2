using System;

namespace OverWatchELD.Services
{
    // Converts real time into "ATS game time" using a scale factor.
    // Your requirement: 1 game hour ≈ 20 real minutes => game runs 3x real time.
    public static class GameClock
    {
        // 1 real second = 3 game seconds
        public const double GameSecondsPerRealSecond = 3.0;

        private static readonly object _lock = new();

        // Anchors: when the app starts, we map real->game
        private static DateTimeOffset _realAnchorUtc = DateTimeOffset.UtcNow;
        private static DateTimeOffset _gameAnchorUtc = DateTimeOffset.UtcNow;

        /// <summary>
        /// Call this when you want to "sync" the in-game clock to a known in-game timestamp.
        /// If you don't have true telemetry game-time, you can still set it once and let it run scaled.
        /// </summary>
        public static void SetGameNow(DateTimeOffset gameNowUtc, DateTimeOffset? realNowUtc = null)
        {
            lock (_lock)
            {
                _realAnchorUtc = realNowUtc ?? DateTimeOffset.UtcNow;
                _gameAnchorUtc = gameNowUtc;
            }
        }

        /// <summary>
        /// In-game "now" in UTC space (scaled from real time).
        /// Use this everywhere instead of DateTimeOffset.UtcNow.
        /// </summary>
        public static DateTimeOffset UtcNow
        {
            get
            {
                lock (_lock)
                {
                    var realElapsed = DateTimeOffset.UtcNow - _realAnchorUtc;
                    var gameElapsed = TimeSpan.FromSeconds(realElapsed.TotalSeconds * GameSecondsPerRealSecond);
                    return _gameAnchorUtc + gameElapsed;
                }
            }
        }
    }
}
