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

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.User.Identity?.Name ??
                                  context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 100
                    }));

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
}
