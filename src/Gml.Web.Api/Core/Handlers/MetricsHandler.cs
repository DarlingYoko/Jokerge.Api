using System.Net;
using Gml.Dto.Messages;
using Gml.Web.Api.Core.Metrics;

namespace Gml.Web.Api.Core.Handlers;

/// <summary>API performance for the panel's «Метрики» page.</summary>
public static class MetricsHandler
{
    public static IResult GetReport(MetricsCollector collector, MetricsLiveProbe probe, string? period)
    {
        if (!MetricsPeriods.TryParse(period ?? "1h", out var parsed))
        {
            return Results.BadRequest(ResponseMessage.Create(
                "Неизвестный период, допустимо: 1h, 6h, 24h", HttpStatusCode.BadRequest));
        }

        var report = collector.BuildReport(parsed);
        report.Live = probe.Capture();

        return Results.Ok(ResponseMessage.Create(report, "Метрики API", HttpStatusCode.OK));
    }
}
