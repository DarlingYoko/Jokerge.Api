namespace Gml.Web.Api.Core.Metrics;

/// <summary>Last gauge sample, for the «now» cards.</summary>
public sealed record MetricsSample(DateTime AtUtc, int LaunchersOnline, double CpuPercent, long MemoryBytes);

/// <summary>
///     In-memory metrics of this API process: a 24 h minute ring fed by <see cref="RequestMetricsMiddleware" /> and
///     <see cref="MetricsBackgroundService" />, plus hours restored from <see cref="MetricsHourStore" /> after a restart.
/// </summary>
public sealed class MetricsCollector
{
    private readonly object _lock = new();
    private readonly Dictionary<DateTime, MetricsBucket> _restoredHours = new();
    private readonly MetricsRing _ring = new();
    private readonly TimeProvider _time;

    public MetricsCollector(TimeProvider time)
    {
        _time = time;
        StartedAtUtc = UtcNow;
    }

    public DateTime StartedAtUtc { get; }

    public MetricsSample? LatestSample { get; private set; }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public void RecordRequest(string route, int statusCode, double ms)
    {
        var now = UtcNow;
        lock (_lock)
            _ring.GetOrCreate(now).RecordRequest(route, statusCode, ms);
    }

    public void RecordSample(int launchersOnline, double cpuPercent, long memoryBytes)
    {
        var now = UtcNow;
        lock (_lock)
        {
            _ring.GetOrCreate(now).RecordSample(launchersOnline, cpuPercent, memoryBytes);
            LatestSample = new MetricsSample(now, launchersOnline, cpuPercent, memoryBytes);
        }
    }

    /// <summary>Hours saved by a previous run; a second restore of the same hour adds to it.</summary>
    public void Restore(IEnumerable<MetricsBucket> hours)
    {
        lock (_lock)
        {
            foreach (var hour in hours)
            {
                if (_restoredHours.TryGetValue(hour.StartUtc, out var existing))
                    existing.Merge(hour);
                else
                    _restoredHours[hour.StartUtc] = hour;
            }
        }
    }

    public MetricsReportDto BuildReport(MetricsPeriod period)
    {
        var now = UtcNow;
        // One extra step so the first, partly covered point gets its data too.
        var from = now.AddMinutes(-MetricsPeriods.LengthMinutes(period) - MetricsPeriods.StepMinutes(period));

        lock (_lock)
        {
            var buckets = _restoredHours.Values
                .Where(h => h.EndUtc > from)
                .Concat(_ring.Range(from, now.AddMinutes(1)));

            return MetricsReportBuilder.Build(buckets, now, period);
        }
    }

    /// <summary>
    ///     Whole hours from <paramref name="fromUtc" /> to now, each = restored part + live minutes, ready to be
    ///     written to disk. New objects: callers may keep or change them.
    /// </summary>
    public IReadOnlyList<MetricsBucket> SnapshotHours(DateTime fromUtc)
    {
        var now = UtcNow;
        var from = MetricsBucket.FloorToHour(fromUtc);

        lock (_lock)
        {
            var hours = new SortedDictionary<DateTime, MetricsBucket>();

            MetricsBucket HourOf(DateTime start)
            {
                if (!hours.TryGetValue(start, out var hour))
                    hours[start] = hour = MetricsBucket.Create(start, 60);
                return hour;
            }

            foreach (var restored in _restoredHours.Values.Where(h => h.StartUtc >= from))
                HourOf(MetricsBucket.FloorToHour(restored.StartUtc)).Merge(restored);

            foreach (var minute in _ring.Range(from, now.AddMinutes(1)))
                HourOf(MetricsBucket.FloorToHour(minute.StartUtc)).Merge(minute);

            return hours.Values.ToList();
        }
    }
}
