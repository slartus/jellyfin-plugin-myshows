using System;
using System.Collections.Concurrent;
using System.Threading;

namespace MyShows
{
    internal static class ScrobbleSuppressor
    {
        private static readonly ConcurrentDictionary<(Guid UserId, Guid ItemId), DateTime> _suppressed = new();
        private static readonly TimeSpan TtlOnMiss = TimeSpan.FromSeconds(30);
        private const int SweepEveryN = 64;
        private static int _suppressCount;

        public static void Suppress(Guid userId, Guid itemId)
        {
            _suppressed[(userId, itemId)] = DateTime.UtcNow.Add(TtlOnMiss);
            if (Interlocked.Increment(ref _suppressCount) % SweepEveryN == 0)
            {
                SweepExpired();
            }
        }

        public static bool TryConsume(Guid userId, Guid itemId)
        {
            if (!_suppressed.TryRemove((userId, itemId), out var expiresAt)) return false;
            if (DateTime.UtcNow > expiresAt) return false;
            return true;
        }

        private static void SweepExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _suppressed)
            {
                if (kvp.Value < now)
                {
                    _suppressed.TryRemove(kvp);
                }
            }
        }
    }
}
