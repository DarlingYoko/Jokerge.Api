using System.Text.Json.Serialization;

namespace Gml.Web.Api.Core.Metrics;

/// <summary>
///     Everything the API saw in one time slice: a live minute in memory, or an hour restored from disk.
///     Gauge samples (launchers, CPU, memory) are taken every few seconds by <see cref="MetricsBackgroundService" />.
/// </summary>
public sealed class MetricsBucket
{
    /// <summary>Scanners hitting random plugin paths must not grow a bucket without limit.</summary>
    public const int MaxRoutes = 200;

    public const string OtherRoutes = "(прочие маршруты)";

    public DateTime StartUtc { get; set; }
    public int Minutes { get; set; }
    public RequestStats Total { get; set; } = new();
    public Dictionary<string, RequestStats> Routes { get; set; } = new();
    public int? LaunchersMax { get; set; }
    public double CpuPercentSum { get; set; }
    public int CpuSamples { get; set; }
    public long? MemoryBytesMax { get; set; }

    /// <summary>
    ///     Minutes of <see cref="Minutes" /> the API was actually running. Null = all of them (live minutes, and
    ///     hours saved before this was tracked).
    /// </summary>
    public int? CoveredMinutes { get; set; }

    [JsonIgnore]
    public int EffectiveCoveredMinutes => CoveredMinutes ?? Minutes;

    [JsonIgnore]
    public DateTime EndUtc => StartUtc.AddMinutes(Minutes);

    [JsonIgnore]
    public double? CpuPercentAvg => CpuSamples == 0 ? null : CpuPercentSum / CpuSamples;

    public static MetricsBucket Create(DateTime startUtc, int minutes) =>
        new() { StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), Minutes = minutes };

    public void RecordRequest(string route, int statusCode, double ms)
    {
        Total.Record(statusCode, ms);

        if (!Routes.TryGetValue(route, out var stats))
        {
            var key = Routes.Count >= MaxRoutes ? OtherRoutes : route;
            if (!Routes.TryGetValue(key, out stats))
                Routes[key] = stats = new RequestStats();
        }

        stats.Record(statusCode, ms);
    }

    public void RecordSample(int launchersOnline, double cpuPercent, long memoryBytes)
    {
        LaunchersMax = Math.Max(LaunchersMax ?? 0, launchersOnline);
        CpuPercentSum += cpuPercent;
        CpuSamples++;
        MemoryBytesMax = Math.Max(MemoryBytesMax ?? 0, memoryBytes);
    }

    /// <summary>
    ///     Adds another bucket's data as is (used to fold minutes into an hour). The other bucket covers different
    ///     minutes, so coverage adds up, capped at this bucket's length.
    /// </summary>
    public void Merge(MetricsBucket other)
    {
        Total.Merge(other.Total);
        CoveredMinutes = Math.Min(Minutes, (CoveredMinutes ?? 0) + other.EffectiveCoveredMinutes);

        foreach (var (route, stats) in other.Routes)
        {
            if (!Routes.TryGetValue(route, out var target))
                Routes[route] = target = new RequestStats();
            target.Merge(stats);
        }

        if (other.LaunchersMax is { } launchers)
            LaunchersMax = Math.Max(LaunchersMax ?? 0, launchers);
        if (other.MemoryBytesMax is { } memory)
            MemoryBytesMax = Math.Max(MemoryBytesMax ?? 0, memory);
        CpuPercentSum += other.CpuPercentSum;
        CpuSamples += other.CpuSamples;
    }

    public static DateTime FloorToMinute(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    public static DateTime FloorToHour(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);
}
