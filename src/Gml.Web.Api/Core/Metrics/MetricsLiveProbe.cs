using Gml.Web.Api.Core.Hubs.Controllers;

namespace Gml.Web.Api.Core.Metrics;

/// <summary>An SQLite file whose size the metrics page shows.</summary>
public sealed record MetricsDatabaseFile(string Name, string Path);

/// <summary>Process state at the moment the panel asks: uptime, memory, last CPU sample, launchers, DB sizes.</summary>
public sealed class MetricsLiveProbe(
    MetricsCollector collector,
    PlayersController players,
    IEnumerable<MetricsDatabaseFile> databases,
    TimeProvider time)
{
    private readonly DateTime _processStartUtc = ProcessMetrics.ProcessStartUtc();

    public MetricsLiveDto Capture()
    {
        var now = time.GetUtcNow().UtcDateTime;

        return new MetricsLiveDto
        {
            StartedAtUtc = _processStartUtc,
            UptimeSeconds = Math.Max(0, (long)(now - _processStartUtc).TotalSeconds),
            WorkingSetBytes = Environment.WorkingSet,
            GcHeapBytes = GC.GetTotalMemory(false),
            MemoryLimitBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            CpuPercent = collector.LatestSample is { } sample ? Math.Round(sample.CpuPercent, 1) : null,
            ProcessorCount = Environment.ProcessorCount,
            LaunchersOnline = players.LauncherInfos.Count,
            Databases = databases
                .DistinctBy(d => Path.GetFullPath(d.Path), StringComparer.OrdinalIgnoreCase)
                .Select(d => ProcessMetrics.MeasureDatabase(d.Name, d.Path))
                .ToList()
        };
    }
}
