using System.Globalization;
using GmlCore.Interfaces.Launcher;

namespace Gml.Web.Api.Core.Sentry;

/// <summary>
///     Builds the "Problems" list: one row per distinct problem with who hit it, when, on which
///     launcher build, and the newest report's full stack.
/// </summary>
public static class SentryIssueBuilder
{
    private const int MaxUsersPerIssue = 50;
    private const int MaxBuckets = 400;

    public static IReadOnlyList<SentryIssueReadDto> Build(IEnumerable<IBugInfo> bugs, SentryTimeRange range,
        int tzOffsetMinutes)
    {
        var events = bugs
            .Where(b => SentryMessageNormalizer.RootException(b) is not null)
            .Select(b => (Bug: b, At: SentryTime.ToUtc(b.SendAt)))
            .Where(e => range.Contains(e.At))
            .ToList();

        if (events.Count == 0)
            return [];

        var bucketing = Bucketing.For(range, events.Min(e => e.At), tzOffsetMinutes);

        return events
            .GroupBy(e => SentryMessageNormalizer.Fingerprint(e.Bug))
            .Select(group => BuildIssue(group.Key, group.ToList(), bucketing, tzOffsetMinutes))
            .OrderByDescending(i => i.Count)
            .ThenByDescending(i => i.LastSeen)
            .ToList();
    }

    private static SentryIssueReadDto BuildIssue(string fingerprint, List<(IBugInfo Bug, DateTime At)> events,
        Bucketing bucketing, int tzOffsetMinutes)
    {
        var latest = events.MaxBy(e => e.At);
        var root = SentryMessageNormalizer.RootException(latest.Bug)!;

        var users = events
            // Two players on one PC and Windows account are two rows, not one.
            .GroupBy(e => (User: Display(e.Bug.Username), Pc: Display(e.Bug.PcName), Player: Clean(e.Bug.Player)))
            .Select(g =>
            {
                var last = g.MaxBy(e => e.At);
                return new SentryIssueUserDto(g.Key.User, g.Key.Pc, g.Key.Player, g.Count(), last.At,
                    Clean(last.Bug.IpAddress), Clean(last.Bug.OsVersion), Clean(last.Bug.Release));
            })
            .OrderByDescending(u => u.Count)
            .ThenByDescending(u => u.LastSeen)
            .ToList();

        return new SentryIssueReadDto(
            fingerprint,
            latest.Bug.ProjectType,
            root.Type ?? "Exception",
            SentryMessageNormalizer.Normalize(root.ValueData),
            events.Count,
            users.Count,
            events.Min(e => e.At),
            latest.At,
            CountBy(events, e => Clean(e.Bug.Release)),
            CountBy(events, e => Clean(e.Bug.OsVersion)),
            users.Take(MaxUsersPerIssue).ToList(),
            bucketing.Name,
            bucketing.Fill(events.Select(e => e.At), tzOffsetMinutes),
            BuildEvent(latest.Bug, latest.At));
    }

    private static SentryIssueEventDto BuildEvent(IBugInfo bug, DateTime at)
    {
        var exceptions = SentryStackTraceFormatter.BuildExceptions(bug.Exceptions);

        return new SentryIssueEventDto(
            bug.Id,
            at,
            Clean(bug.Username),
            Clean(bug.PcName),
            Clean(bug.Player),
            Clean(bug.IpAddress),
            Clean(bug.OsVersion),
            Clean(bug.OsIdentifier),
            Clean(bug.Release),
            bug.MemoryInfo?.AllocatedBytes,
            bug.MemoryInfo?.TotalAvailableMemoryBytes,
            exceptions,
            SentryStackTraceFormatter.Format(exceptions));
    }

    private static IReadOnlyList<SentryCountDto> CountBy(IEnumerable<(IBugInfo Bug, DateTime At)> events,
        Func<(IBugInfo Bug, DateTime At), string?> key) =>
        events
            .Select(key)
            .Where(k => k is not null)
            .GroupBy(k => k!)
            .Select(g => new SentryCountDto(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value is "Not found" or "Not Found" ? null : value;

    private static string Display(string? value) => Clean(value) ?? "—";

    /// <summary>Hourly bars for a day or two, daily up to a few months, weekly beyond.</summary>
    private sealed record Bucketing(string Name, DateTime FirstViewer, DateTime LastViewer)
    {
        public static Bucketing For(SentryTimeRange range, DateTime earliestEventUtc, int tz)
        {
            // "All time" starts at DateTime.MinValue; chart from the first report instead.
            var fromUtc = range.FromUtc < earliestEventUtc.AddYears(-1) ? earliestEventUtc : range.FromUtc;
            var span = range.ToUtc - fromUtc;
            var name = span <= TimeSpan.FromHours(48) ? "hour" : span <= TimeSpan.FromDays(120) ? "day" : "week";

            var first = Floor(SentryTime.ToViewer(fromUtc, tz), name);
            var last = Floor(SentryTime.ToViewer(range.ToUtc, tz), name);
            return new Bucketing(name, first, last);
        }

        public IReadOnlyList<SentryBucketDto> Fill(IEnumerable<DateTime> eventsUtc, int tz)
        {
            var counts = eventsUtc
                .GroupBy(at => Floor(SentryTime.ToViewer(at, tz), Name))
                .ToDictionary(g => g.Key, g => g.Count());

            var buckets = new List<SentryBucketDto>();
            for (var at = FirstViewer; at <= LastViewer && buckets.Count < MaxBuckets; at = Next(at))
                buckets.Add(new SentryBucketDto(Label(at), counts.GetValueOrDefault(at)));

            return buckets;
        }

        private DateTime Next(DateTime at) => Name switch
        {
            "hour" => at.AddHours(1),
            "day" => at.AddDays(1),
            _ => at.AddDays(7)
        };

        private string Label(DateTime at) => Name == "hour"
            ? at.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)
            : at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static DateTime Floor(DateTime viewer, string name) => name switch
        {
            "hour" => new DateTime(viewer.Year, viewer.Month, viewer.Day, viewer.Hour, 0, 0),
            "day" => viewer.Date,
            _ => viewer.Date.AddDays(-(((int)viewer.DayOfWeek + 6) % 7))
        };
    }
}
