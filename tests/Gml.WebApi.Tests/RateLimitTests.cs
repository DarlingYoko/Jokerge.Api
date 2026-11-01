using System.Net;
using System.Threading.RateLimiting;
using Gml.Web.Api.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gml.WebApi.Tests;

/// <summary>
///     The API sits behind Nginx Proxy Manager and Gml.Web.Proxy, so without forwarded headers every
///     anonymous request (authlib, textures, the proxy's own /health probe) shared one rate-limit
///     bucket keyed by the proxy container's IP. With 20 players online that bucket ran dry, /health
///     queued past the proxy's 3 s probe timeout, and the proxy redirected every request to /wait.
/// </summary>
public class RateLimitTests
{
    private static readonly IPAddress ProxyContainer = IPAddress.Parse("172.18.0.7");
    private static readonly IPAddress PlayerA = IPAddress.Parse("93.159.55.223");
    private static readonly IPAddress PlayerB = IPAddress.Parse("176.221.0.145");

    [Test]
    public async Task ForwardedHeaders_TakesClientIpFromTrustedProxy()
    {
        HttpContext context = Request("/api/v1/profiles/info", ProxyContainer);
        context.Request.Headers["X-Forwarded-For"] = PlayerA.ToString();

        await RunForwardedHeaders(context);

        Assert.That(context.Connection.RemoteIpAddress, Is.EqualTo(PlayerA));
    }

    [Test]
    public async Task ForwardedHeaders_IgnoresHeaderFromUntrustedPeer()
    {
        HttpContext context = Request("/api/v1/profiles/info", PlayerA);
        context.Request.Headers["X-Forwarded-For"] = PlayerB.ToString();

        await RunForwardedHeaders(context);

        Assert.That(context.Connection.RemoteIpAddress, Is.EqualTo(PlayerA));
    }

    [Test]
    public async Task ForwardedHeaders_LeavesForwardedHostForHandlers()
    {
        // Handlers build public URLs from X-Forwarded-Host/-Scheme themselves; the middleware must
        // not consume those headers.
        HttpContext context = Request("/api/v1/profiles/info", ProxyContainer);
        context.Request.Headers["X-Forwarded-For"] = PlayerA.ToString();
        context.Request.Headers["X-Forwarded-Host"] = "launcher.example.com";

        await RunForwardedHeaders(context);

        Assert.That(context.Request.Headers["X-Forwarded-Host"].ToString(), Is.EqualTo("launcher.example.com"));
    }

    [Test]
    public void GlobalLimiter_NeverLimitsHealthChecks()
    {
        using PartitionedRateLimiter<HttpContext> limiter = RateLimitExtension.CreateGlobalLimiter();

        for (int i = 0; i < 1000; i++)
        {
            using RateLimitLease lease = limiter.AttemptAcquire(Request("/health", ProxyContainer));
            Assert.That(lease.IsAcquired, Is.True, $"health probe #{i} was limited");
        }
    }

    [Test]
    public void GlobalLimiter_GivesGameTrafficItsOwnBudget()
    {
        using PartitionedRateLimiter<HttpContext> limiter = RateLimitExtension.CreateGlobalLimiter();
        Exhaust(limiter, "/api/v1/profiles/info", PlayerA);

        using RateLimitLease authlib = limiter.AttemptAcquire(
            Request("/api/v1/integrations/authlib/minecraft/sessionserver/session/minecraft/hasJoined", PlayerA));
        using RateLimitLease skin = limiter.AttemptAcquire(
            Request("/api/v1/integrations/texture/skins/0d6b1a4e", PlayerA));

        Assert.Multiple(() =>
        {
            Assert.That(authlib.IsAcquired, Is.True);
            Assert.That(skin.IsAcquired, Is.True);
        });
    }

    [Test]
    public void GlobalLimiter_GameTrafficAllowsAFullServerOfSkinLookups()
    {
        // Each client fetches the profile and skin of every other player it sees.
        using PartitionedRateLimiter<HttpContext> limiter = RateLimitExtension.CreateGlobalLimiter();

        for (int i = 0; i < 500; i++)
        {
            using RateLimitLease lease = limiter.AttemptAcquire(
                Request($"/api/v1/integrations/authlib/minecraft/sessionserver/session/minecraft/profile/{i}", PlayerA));
            Assert.That(lease.IsAcquired, Is.True, $"lookup #{i} was limited");
        }
    }

    [Test]
    public void GlobalLimiter_StillLimitsOtherAnonymousRequestsPerClient()
    {
        using PartitionedRateLimiter<HttpContext> limiter = RateLimitExtension.CreateGlobalLimiter();
        Exhaust(limiter, "/api/v1/profiles/info", PlayerA);

        using RateLimitLease sameClient = limiter.AttemptAcquire(Request("/api/v1/profiles/info", PlayerA));
        using RateLimitLease otherClient = limiter.AttemptAcquire(Request("/api/v1/profiles/info", PlayerB));

        Assert.Multiple(() =>
        {
            Assert.That(sameClient.IsAcquired, Is.False);
            Assert.That(otherClient.IsAcquired, Is.True);
        });
    }

    private static void Exhaust(PartitionedRateLimiter<HttpContext> limiter, string path, IPAddress client)
    {
        for (int i = 0; i < 1000; i++)
        {
            using RateLimitLease lease = limiter.AttemptAcquire(Request(path, client));
            if (!lease.IsAcquired) return;
        }

        Assert.Fail($"{path} was never limited");
    }

    private static DefaultHttpContext Request(string path, IPAddress remote)
    {
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = remote;
        return context;
    }

    private static Task RunForwardedHeaders(HttpContext context)
    {
        ForwardedHeadersMiddleware middleware = new(_ => Task.CompletedTask, NullLoggerFactory.Instance,
            Options.Create(ForwardedHeadersExtension.CreateOptions()));
        return middleware.Invoke(context);
    }
}
