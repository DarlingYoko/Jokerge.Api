using System.Diagnostics;

namespace Gml.Web.Api.Core.Metrics;

/// <summary>Times every HTTP request (except SignalR hubs and /health) and files it under its route template.</summary>
public sealed class RequestMetricsMiddleware(RequestDelegate next, MetricsCollector collector)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!RouteNormalizer.IsTracked(context.Request.Path.Value ?? string.Empty))
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var failed = false;

        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var status = failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            var pattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            var route = RouteNormalizer.Normalize(context.Request.Method, pattern,
                context.Request.Path.Value ?? string.Empty, status);

            collector.RecordRequest(route, status, elapsedMs);
        }
    }
}
