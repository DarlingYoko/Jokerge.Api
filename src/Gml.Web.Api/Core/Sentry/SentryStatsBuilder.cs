using System.Globalization;
using GmlCore.Interfaces.Launcher;
using GmlCore.Interfaces.Sentry;

namespace Gml.Web.Api.Core.Sentry;

/// <summary>Numbers for the overview cards and the 90-day chart.</summary>
public static class SentryStatsBuilder
{
    /// <summary>How many days of reports <see cref="BuildSummary" /> needs.</summary>
    public const int SummaryDays = 15;

    public static SentrySummaryReadDto BuildSummary(IEnumerable<IBugInfo> bugs, int totalBugs, DateTime nowUtc,
        int tzOffsetMinutes)
    {
        var events = bugs.Select(b => (Bug: b, At: SentryTime.ToUtc(b.SendAt))).Where(e => e.At <= nowUtc).ToList();

        var today = SentryTime.ToViewer(nowUtc, tzOffsetMinutes).Date;
        var yesterday = today.AddDays(-1);
        var weekAgo = nowUtc.AddDays(-7);
        var twoWeeksAgo = nowUtc.AddDays(-14);

        var lastWeek = events.Where(e => e.At > weekAgo).ToList();

        return new SentrySummaryReadDto(
            totalBugs,
            events.Count(e => SentryTime.ToViewer(e.At, tzOffsetMinutes).Date == today),
            events.Count(e => SentryTime.ToViewer(e.At, tzOffsetMinutes).Date == yesterday),
            lastWeek.Count,
            events.Count(e => e.At > twoWeeksAgo && e.At <= weekAgo),
            lastWeek
                .Where(e => SentryMessageNormalizer.RootException(e.Bug) is not null)
                .Select(e => SentryMessageNormalizer.Fingerprint(e.Bug))
                .Distinct()
                .Count(),
            lastWeek.Select(e => (e.Bug.Username, e.Bug.PcName)).Distinct().Count(),
            events.Count == 0 ? null : events.Max(e => e.At));
    }

    public static IReadOnlyList<SentryDailyStatsDto> BuildDaily(IEnumerable<IBugInfo> bugs, DateTime nowUtc,
        int tzOffsetMinutes, int days = 90)
    {
        var today = SentryTime.ToViewer(nowUtc, tzOffsetMinutes).Date;
        var first = today.AddDays(-(days - 1));

        var counts = bugs
            .Select(b => (b.ProjectType, Day: SentryTime.ToViewer(SentryTime.ToUtc(b.SendAt), tzOffsetMinutes).Date))
            .Where(e => e.Day >= first && e.Day <= today)
            .GroupBy(e => e.Day)
            .ToDictionary(
                g => g.Key,
                g => (Launcher: g.Count(e => e.ProjectType == ProjectType.Launcher),
                    Backend: g.Count(e => e.ProjectType == ProjectType.Backend)));

        return Enumerable.Range(0, days)
            .Select(offset => first.AddDays(offset))
            .Select(day =>
            {
                var (launcher, backend) = counts.GetValueOrDefault(day);
                return new SentryDailyStatsDto(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), launcher,
                    backend);
            })
            .ToList();
    }
}
