using GmlCore.Interfaces.Sentry;

namespace Gml.Web.Api.Core.Sentry;

/// <summary>Body of <c>POST /api/v1/sentry/filter/list</c>.</summary>
public class SentryIssuesFilterDto
{
    public ProjectType ProjectType { get; set; } = ProjectType.All;
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }

    /// <summary>Viewer offset in minutes east of UTC (Moscow is 180).</summary>
    public int? TzOffset { get; set; }
}

/// <summary>Body of <c>POST /api/v1/sentry/issues/resolve</c>.</summary>
public class SentryResolveIssueDto
{
    public string Fingerprint { get; set; } = string.Empty;
}

public record SentryCountDto(string Name, int Count);

/// <summary>One bar of a chart; <see cref="Start" /> is viewer wall-clock time.</summary>
public record SentryBucketDto(string Start, int Count);

public record SentryFrameDto(string? Function, string? File, int? Line, bool InApp);

public record SentryExceptionDto(string Type, string Message, IReadOnlyList<SentryFrameDto> Frames);

public record SentryIssueUserDto(
    string Username,
    string PcName,
    int Count,
    DateTime LastSeen,
    string? IpAddress,
    string? Os,
    string? Release);

public record SentryIssueEventDto(
    string Id,
    DateTime SendAt,
    string? Username,
    string? PcName,
    string? IpAddress,
    string? Os,
    string? Runtime,
    string? Release,
    long? MemoryAllocatedBytes,
    long? MemoryTotalBytes,
    IReadOnlyList<SentryExceptionDto> Exceptions,
    string StackTrace);

public record SentryIssueReadDto(
    string Fingerprint,
    ProjectType ProjectType,
    string Exception,
    string Message,
    int Count,
    int CountUsers,
    DateTime FirstSeen,
    DateTime LastSeen,
    IReadOnlyList<SentryCountDto> Releases,
    IReadOnlyList<SentryCountDto> OperationSystems,
    IReadOnlyList<SentryIssueUserDto> Users,
    string Granularity,
    IReadOnlyList<SentryBucketDto> Graphic,
    SentryIssueEventDto LatestEvent);

public record SentrySummaryReadDto(
    int TotalBugs,
    int BugsToday,
    int BugsYesterday,
    int BugsLast7Days,
    int BugsPrevious7Days,
    int IssuesLast7Days,
    int UsersLast7Days,
    DateTime? LastBugAt);

public record SentryDailyStatsDto(string Date, int Launcher, int Backend);
