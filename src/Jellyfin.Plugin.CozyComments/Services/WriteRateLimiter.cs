using System.Collections.Concurrent;

namespace Jellyfin.Plugin.CozyComments.Services;

/// <summary>In-memory per-user write limit (comments and edits), configured in the plugin settings.</summary>
public sealed class WriteRateLimiter
{
    private readonly ConcurrentDictionary<Guid, Queue<DateTime>> _hits = new();

    /// <summary>Records a write if the user is under both limits.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="perMinute">Max writes per minute (0 = unlimited).</param>
    /// <param name="perHour">Max writes per hour (0 = unlimited).</param>
    /// <returns>False if the user is over a limit.</returns>
    public bool TryHit(Guid userId, int perMinute, int perHour)
    {
        var now = DateTime.UtcNow;
        var q = _hits.GetOrAdd(userId, _ => new Queue<DateTime>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > TimeSpan.FromHours(1))
            {
                q.Dequeue();
            }

            var lastMinute = q.Count(t => now - t < TimeSpan.FromMinutes(1));
            if ((perMinute > 0 && lastMinute >= perMinute) || (perHour > 0 && q.Count >= perHour))
            {
                return false;
            }

            q.Enqueue(now);
            return true;
        }
    }
}
