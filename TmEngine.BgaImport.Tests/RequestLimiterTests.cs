using System.Net;
using Microsoft.AspNetCore.Http;
using tm_engine.Api;

namespace TmEngine.BgaImport.Tests;

public class RequestLimiterTests
{
    private static HttpRequest From(string ip, string? forwardedFor = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (forwardedFor != null)
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        return context.Request;
    }

    [Fact]
    public void AllowsUpToTheLimitPerClientPerDay_ThenRefuses()
    {
        var limiter = new RequestLimiter();

        Assert.True(limiter.Allow(From("10.0.0.1"), "import", 2));
        Assert.True(limiter.Allow(From("10.0.0.1"), "import", 2));
        Assert.False(limiter.Allow(From("10.0.0.1"), "import", 2));

        // Other clients and other buckets are counted separately.
        Assert.True(limiter.Allow(From("10.0.0.2"), "import", 2));
        Assert.True(limiter.Allow(From("10.0.0.1"), "fetch", 2));
    }

    [Fact]
    public void StartsOverOnANewDay()
    {
        var now = new DateTime(2026, 9, 30, 23, 59, 0, DateTimeKind.Utc);
        var limiter = new RequestLimiter(() => now);

        Assert.True(limiter.Allow(From("10.0.0.1"), "import", 1));
        Assert.False(limiter.Allow(From("10.0.0.1"), "import", 1));

        now = now.AddMinutes(2);
        Assert.True(limiter.Allow(From("10.0.0.1"), "import", 1));
    }

    [Fact]
    public void IdentifiesTheClientByTheForwardedAddressBehindAProxy()
    {
        var limiter = new RequestLimiter();

        // Two visitors through the same proxy are told apart; the same visitor is recognised across hops.
        Assert.True(limiter.Allow(From("10.0.0.9", "203.0.113.5:4711, 10.0.0.9"), "import", 1));
        Assert.True(limiter.Allow(From("10.0.0.9", "203.0.113.6, 10.0.0.9"), "import", 1));
        Assert.False(limiter.Allow(From("10.0.0.9", "203.0.113.5"), "import", 1));
    }
}
