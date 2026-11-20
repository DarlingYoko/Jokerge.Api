namespace Gml.Web.Api.Core.Metrics;

/// <summary>
///     Folds minute buckets (and hours restored from disk) into chart points, totals and the endpoint table.
///     A bucket that only partly overlaps a point counts with the overlapping share, so a restored hour is spread
///     evenly over its quarter-hours instead of landing in one spike.
/// </summary>
public static class MetricsReportBuilder
{
    public const int MaxEndpoints = 100;

    public static MetricsReportDto Build(IEnumerable<MetricsBucket> buckets, DateTime nowUtc, MetricsPeriod period)
    {
        var step = MetricsPeriods.StepMinutes(period);
        var pointCount = MetricsPeriods.LengthMinutes(period) / step;

        var lastPoint = FloorToStep(nowUtc, step);
        var from = lastPoint.AddMinutes(-(pointCount - 1) * step);
        var windowEnd = lastPoint.AddMinutes(step);

        var relevant = buckets.Where(b => b.EndUtc > from && b.StartUtc < windowEnd).ToList();

        var series = Enumerable.Range(0, pointCount)
            .Select(i => from.AddMinutes(i * step))
            .Select(start => BuildPoint(Aggregate(relevant, start, start.AddMinutes(step), false), start))
            .ToList();

        var whole = Aggregate(relevant, from, windowEnd, true);

        return new MetricsReportDto
        {
            Period = MetricsPeriods.Code(period),
            FromUtc = from,
            ToUtc = nowUtc,
            StepMinutes = step,
            Summary = BuildSummary(whole),
            Series = series,
            Endpoints = whole.Routes
                .Select(route => BuildEndpoint(route.Key, route.Value, whole.CoveredMinutes))
                .Where(e => e.Requests > 0)
                .OrderByDescending(e => e.Requests)
                .ThenBy(e => e.Route, StringComparer.Ordinal)
                .Take(MaxEndpoints)
                .ToList()
        };
    }

    private static MetricsSummaryDto BuildSummary(Slice data)
    {
        var total = data.Total;
        var hasRequests = total.Count > 0;

        return new MetricsSummaryDto
        {
            Requests = RoundCount(total.Count),
            CoveredMinutes = (int)Math.Round(data.CoveredMinutes),
            RequestsPerMinute = PerMinute(total.Count, data.CoveredMinutes),
            P50Ms = RoundMs(total.Latency.Percentile(0.5)),
            P95Ms = RoundMs(total.Latency.Percentile(0.95)),
            ClientErrors = RoundCount(total.ClientErrors),
            ServerErrors = RoundCount(total.ServerErrors),
            ClientErrorRate = hasRequests ? Math.Round(total.ClientErrors / total.Count, 4) : null,
            ServerErrorRate = hasRequests ? Math.Round(total.ServerErrors / total.Count, 4) : null,
            PeakLaunchersOnline = data.LaunchersMax
        };
    }

    private static MetricsPointDto BuildPoint(Slice data, DateTime start)
    {
        if (data.CoveredMinutes <= 0)
            return new MetricsPointDto { TimeUtc = start };

        return new MetricsPointDto
        {
            TimeUtc = start,
            Requests = RoundCount(data.Total.Count),
            RequestsPerMinute = PerMinute(data.Total.Count, data.CoveredMinutes),
            P50Ms = RoundMs(data.Total.Latency.Percentile(0.5)),
            P95Ms = RoundMs(data.Total.Latency.Percentile(0.95)),
            ClientErrors = RoundCount(data.Total.ClientErrors),
            ServerErrors = RoundCount(data.Total.ServerErrors),
            LaunchersOnline = data.LaunchersMax,
            CpuPercent = data.CpuSamples == 0 ? null : Math.Round(data.CpuSum / data.CpuSamples, 1),
            MemoryBytes = data.MemoryMax
        };
    }

    private static MetricsEndpointDto BuildEndpoint(string route, RequestStats stats, double coveredMinutes) => new()
    {
        Route = route,
        Requests = RoundCount(stats.Count),
        RequestsPerMinute = PerMinute(stats.Count, coveredMinutes),
        P50Ms = RoundMs(stats.Latency.Percentile(0.5)),
        P95Ms = RoundMs(stats.Latency.Percentile(0.95)),
        MaxMs = Math.Round(stats.Latency.MaxMs, 1),
        ClientErrors = RoundCount(stats.ClientErrors),
        ServerErrors = RoundCount(stats.ServerErrors)
    };

    private static Slice Aggregate(IReadOnlyList<MetricsBucket> buckets, DateTime start, DateTime end,
        bool withRoutes)
    {
        var result = new Slice();
        var liveMinutes = new HashSet<long>();
        double historyMinutes = 0;

        foreach (var bucket in buckets)
        {
            var overlapStart = bucket.StartUtc > start ? bucket.StartUtc : start;
            var overlapEnd = bucket.EndUtc < end ? bucket.EndUtc : end;
            if (overlapEnd <= overlapStart)
                continue;

            var weight = (overlapEnd - overlapStart).TotalMinutes / bucket.Minutes;

            result.Total.Merge(bucket.Total, weight);

            if (withRoutes)
            {
                foreach (var (route, stats) in bucket.Routes)
                {
                    if (!result.Routes.TryGetValue(route, out var target))
                        result.Routes[route] = target = new RequestStats();
                    target.Merge(stats, weight);
                }
            }

            if (bucket.LaunchersMax is { } launchers)
                result.LaunchersMax = Math.Max(result.LaunchersMax ?? 0, launchers);
            if (bucket.MemoryBytesMax is { } memory)
                result.MemoryMax = Math.Max(result.MemoryMax ?? 0, memory);
            result.CpuSum += bucket.CpuPercentSum;
            result.CpuSamples += bucket.CpuSamples;

            // A history hour knows how many of its minutes the API was up, not which ones: spread them evenly.
            // Those minutes were before a restart, so they never coincide with live minutes of the same hour.
            if (bucket.Minutes == 1)
                liveMinutes.Add(bucket.StartUtc.Ticks / TimeSpan.TicksPerMinute);
            else
                historyMinutes += weight * bucket.EffectiveCoveredMinutes;
        }

        result.CoveredMinutes = liveMinutes.Count + historyMinutes;
        return result;
    }

    private static DateTime FloorToStep(DateTime utc, int stepMinutes)
    {
        var stepTicks = TimeSpan.TicksPerMinute * stepMinutes;
        return new DateTime(utc.Ticks - utc.Ticks % stepTicks, DateTimeKind.Utc);
    }

    private static long RoundCount(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    private static double? RoundMs(double? value) => value is null ? null : Math.Round(value.Value, 1);

    private static double? PerMinute(double count, double minutes) =>
        minutes <= 0 ? null : Math.Round(count / minutes, 2);

    private sealed class Slice
    {
        public RequestStats Total { get; } = new();
        public Dictionary<string, RequestStats> Routes { get; } = new();
        public double CoveredMinutes { get; set; }
        public int? LaunchersMax { get; set; }
        public long? MemoryMax { get; set; }
        public double CpuSum { get; set; }
        public int CpuSamples { get; set; }
    }
}
