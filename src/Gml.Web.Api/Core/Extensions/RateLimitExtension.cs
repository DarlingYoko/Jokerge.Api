using System.Threading.RateLimiting;

namespace Gml.Web.Api.Core.Extensions;

public static class RateLimitExtension
{
    public const string AuthPolicy = "auth";

    // A single install/update can legitimately fetch far more than 100 files (the GlobalLimiter's
    // per-minute budget shared by every other endpoint) — launcher clients download many files
    // concurrently, so this bounds simultaneous in-flight downloads per client instead of requests
    // per time window. Combined with .DisableRateLimiting() on the file endpoint (which opts it out
    // of the GlobalLimiter), this is the only limiter that applies to downloads.
    public const string DownloadPolicy = "downloads";

    public static IServiceCollection ConfigureRateLimit(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = CreateGlobalLimiter();

            // Tighter brute-force limiter for signin/refresh, keyed by client IP.
            options.AddPolicy(AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(DownloadPolicy, context =>
                RateLimitPartition.GetConcurrencyLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    factory: _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 32,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 500
                    }));
        });

        return services;
    }

    // Game clients look up the authlib profile and skin of every player they see, and the game server
    // checks every login, so this traffic gets its own, larger per-client budget instead of eating
    // into the launcher's 100 requests per minute.
    private static readonly PathString[] GameTrafficPrefixes =
    [
        "/api/v1/integrations/authlib",
        "/api/v1/integrations/texture"
    ];

    public static PartitionedRateLimiter<HttpContext> CreateGlobalLimiter()
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path;

            // Gml.Web.Proxy probes /health every 3 s with a 3 s timeout and redirects every request to
            // /wait once the probe fails, so a queued probe takes the whole API offline.
            if (path.StartsWithSegments("/health"))
                return RateLimitPartition.GetNoLimiter("health");

            var client = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

            if (GameTrafficPrefixes.Any(prefix => path.StartsWithSegments(prefix)))
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"game:{client}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1000,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 100
                    });

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.User.Identity?.Name ?? client,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 100
                });
        });
    }
}
