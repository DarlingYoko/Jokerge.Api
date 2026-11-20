namespace Gml.Web.Api.Core.Metrics;

/// <summary>
///     One bucket per minute for the last <c>capacityMinutes</c> (24 h by default). A slot is reused when its
///     minute comes round again, so memory stays flat. Not thread-safe: <see cref="MetricsCollector" /> locks.
/// </summary>
public sealed class MetricsRing
{
    public const int DayMinutes = 24 * 60;

    private readonly MetricsBucket?[] _slots;

    public MetricsRing(int capacityMinutes = DayMinutes)
    {
        _slots = new MetricsBucket?[capacityMinutes];
    }

    public MetricsBucket GetOrCreate(DateTime utc)
    {
        var minute = MetricsBucket.FloorToMinute(utc);
        var slot = (int)(minute.Ticks / TimeSpan.TicksPerMinute % _slots.Length);

        var bucket = _slots[slot];
        if (bucket is null || bucket.StartUtc != minute)
            _slots[slot] = bucket = MetricsBucket.Create(minute, 1);

        return bucket;
    }

    /// <summary>Minutes starting in [<paramref name="fromUtc" />, <paramref name="toUtc" />), oldest first.</summary>
    public IEnumerable<MetricsBucket> Range(DateTime fromUtc, DateTime toUtc)
    {
        var from = MetricsBucket.FloorToMinute(fromUtc);

        return _slots
            .OfType<MetricsBucket>()
            .Where(b => b.StartUtc >= from && b.StartUtc < toUtc)
            .OrderBy(b => b.StartUtc);
    }
}
