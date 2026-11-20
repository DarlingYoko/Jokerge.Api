namespace Gml.Web.Api.Core.Metrics;

/// <summary><c>GET /api/v1/metrics</c>: what the panel's «Метрики» page draws.</summary>
public sealed class MetricsReportDto
{
    /// <summary>1h, 6h or 24h.</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>Start of the first chart point.</summary>
    public DateTime FromUtc { get; set; }

    public DateTime ToUtc { get; set; }
    public int StepMinutes { get; set; }
    public MetricsSummaryDto Summary { get; set; } = new();
    public List<MetricsPointDto> Series { get; set; } = [];

    /// <summary>Busiest routes first, at most <see cref="MetricsReportBuilder.MaxEndpoints" />.</summary>
    public List<MetricsEndpointDto> Endpoints { get; set; } = [];

    /// <summary>Process state right now; filled by the handler, not by the builder.</summary>
    public MetricsLiveDto Live { get; set; } = new();
}

public sealed class MetricsSummaryDto
{
    public long Requests { get; set; }

    /// <summary>Minutes of the period the API was running (and so collecting). Null rates below mean no data.</summary>
    public int CoveredMinutes { get; set; }

    public double? RequestsPerMinute { get; set; }
    public double? P50Ms { get; set; }
    public double? P95Ms { get; set; }
    public long ClientErrors { get; set; }
    public long ServerErrors { get; set; }

    /// <summary>0..1</summary>
    public double? ClientErrorRate { get; set; }

    /// <summary>0..1</summary>
    public double? ServerErrorRate { get; set; }

    public int? PeakLaunchersOnline { get; set; }
}

/// <summary>One chart point. All values are null when the API was not running at that time.</summary>
public sealed class MetricsPointDto
{
    public DateTime TimeUtc { get; set; }
    public long? Requests { get; set; }
    public double? RequestsPerMinute { get; set; }
    public double? P50Ms { get; set; }
    public double? P95Ms { get; set; }
    public long? ClientErrors { get; set; }
    public long? ServerErrors { get; set; }
    public int? LaunchersOnline { get; set; }
    public double? CpuPercent { get; set; }
    public long? MemoryBytes { get; set; }
}

public sealed class MetricsEndpointDto
{
    /// <summary>"GET /api/v1/profiles/{profileName}" or <see cref="RouteNormalizer.Unmatched" />.</summary>
    public string Route { get; set; } = string.Empty;

    public long Requests { get; set; }
    public double? RequestsPerMinute { get; set; }
    public double? P50Ms { get; set; }
    public double? P95Ms { get; set; }
    public double MaxMs { get; set; }
    public long ClientErrors { get; set; }
    public long ServerErrors { get; set; }
}

public sealed class MetricsLiveDto
{
    public DateTime StartedAtUtc { get; set; }
    public long UptimeSeconds { get; set; }
    public long WorkingSetBytes { get; set; }
    public long GcHeapBytes { get; set; }

    /// <summary>Memory the runtime may use: the container limit or the machine's RAM.</summary>
    public long MemoryLimitBytes { get; set; }

    /// <summary>Last sample, share of all cores; null until the first sample.</summary>
    public double? CpuPercent { get; set; }

    public int ProcessorCount { get; set; }
    public int LaunchersOnline { get; set; }
    public List<MetricsDatabaseDto> Databases { get; set; } = [];
}

public sealed class MetricsDatabaseDto
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    /// <summary>File plus its -wal journal; null when the file does not exist.</summary>
    public long? Bytes { get; set; }
}
