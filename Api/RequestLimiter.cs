using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace tm_engine.Api;

/// <summary>
/// Caps how often one client may do something per UTC day: importing games and fetching logs from the
/// registry are open to the site's visitors, and this keeps a script from filling the store or hammering
/// the registry. Counts live in memory, so they are per instance and start over on a cold start; that
/// is a soft limit, which is all that is needed against casual abuse.
/// </summary>
public class RequestLimiter
{
    private readonly ConcurrentDictionary<(string Bucket, string Day, string Client), int> _counts = new();
    private readonly Func<DateTime> _now;

    public RequestLimiter() : this(() => DateTime.UtcNow) { }

    /// <summary>With a clock of the test's choosing. Not for the container, which would hand it a Func of its own.</summary>
    internal RequestLimiter(Func<DateTime> now)
    {
        _now = now;
    }

    /// <summary>Counts one request in <paramref name="bucket"/> for the caller; false once they are over <paramref name="dailyLimit"/>.</summary>
    public bool Allow(HttpRequest req, string bucket, int dailyLimit)
    {
        var today = _now().ToString("yyyy-MM-dd");
        var key = (bucket, today, ClientOf(req));
        var count = _counts.AddOrUpdate(key, 1, (_, n) => n + 1);

        // Yesterday's counts are no longer needed; drop them the first time a new day is seen.
        foreach (var stale in _counts.Keys.Where(k => k.Day != today))
            _counts.TryRemove(stale, out _);

        return count <= dailyLimit;
    }

    /// <summary>The limit named by an app setting, or the default when it is not set.</summary>
    public static int LimitSetting(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var limit) ? limit : fallback;

    /// <summary>The caller's address: the first hop in X-Forwarded-For behind Front Door, else the connection's.</summary>
    private static string ClientOf(HttpRequest req)
    {
        var forwarded = req.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(forwarded))
            return forwarded.Split(',')[0].Trim().Split(':')[0];
        return req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
