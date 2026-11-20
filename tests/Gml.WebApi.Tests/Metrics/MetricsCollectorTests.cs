using Gml.Web.Api.Core.Metrics;

namespace Gml.WebApi.Tests.Metrics;

public class MetricsCollectorTests
{
    private static readonly DateTime Start = new(2026, 11, 20, 12, 10, 0, DateTimeKind.Utc);

    [Test]
    public void RecordedRequests_ShowUpInTheReport()
    {
        var clock = new ManualTimeProvider(Start);
        var collector = new MetricsCollector(clock);

        collector.RecordRequest("GET /a", 200, 15);
        clock.Advance(TimeSpan.FromSeconds(30));

        var report = collector.BuildReport(MetricsPeriod.Hour);

        Assert.Multiple(() =>
        {
            Assert.That(report.Summary.Requests, Is.EqualTo(1));
            Assert.That(report.Endpoints.Single().Route, Is.EqualTo("GET /a"));
            Assert.That(collector.StartedAtUtc, Is.EqualTo(Start));
        });
    }

    [Test]
    public void LatestSample_IsKeptForTheLiveCards()
    {
        var collector = new MetricsCollector(new ManualTimeProvider(Start));

        collector.RecordSample(4, 12.5, 1000);
        collector.RecordSample(6, 20, 2000);

        Assert.Multiple(() =>
        {
            Assert.That(collector.LatestSample?.LaunchersOnline, Is.EqualTo(6));
            Assert.That(collector.LatestSample?.CpuPercent, Is.EqualTo(20));
        });
    }

    [Test]
    public void RestoredHistory_IsPartOfTheDayReport()
    {
        var clock = new ManualTimeProvider(Start);
        var collector = new MetricsCollector(clock);
        var yesterdayEvening = MetricsBucket.Create(Start.Date.AddHours(-2), 60);
        for (var i = 0; i < 120; i++) yesterdayEvening.RecordRequest("GET /a", 200, 10);
        var tooOld = MetricsBucket.Create(Start.AddHours(-30), 60);
        tooOld.RecordRequest("GET /a", 200, 10);

        collector.Restore([yesterdayEvening, tooOld]);

        Assert.That(collector.BuildReport(MetricsPeriod.Day).Summary.Requests, Is.EqualTo(120));
    }

    [Test]
    public void HourSnapshot_AddsLiveMinutesToTheHourRestoredFromDisk()
    {
        // The API restarted at 12:10: 12:00–12:10 is on disk, the rest of the hour is in memory.
        var clock = new ManualTimeProvider(Start);
        var collector = new MetricsCollector(clock);
        var hourStart = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        var fromDisk = MetricsBucket.Create(hourStart, 60);
        for (var i = 0; i < 100; i++) fromDisk.RecordRequest("GET /a", 200, 10);
        fromDisk.CoveredMinutes = 10;
        collector.Restore([fromDisk]);

        for (var i = 0; i < 5; i++) collector.RecordRequest("GET /b", 200, 10);
        clock.Advance(TimeSpan.FromMinutes(20));
        collector.RecordRequest("GET /b", 500, 10);

        var hour = collector.SnapshotHours(hourStart).Single();

        Assert.Multiple(() =>
        {
            Assert.That(hour.StartUtc, Is.EqualTo(hourStart));
            Assert.That(hour.Minutes, Is.EqualTo(60));
            Assert.That(hour.Total.Count, Is.EqualTo(106));
            Assert.That(hour.Total.ServerErrors, Is.EqualTo(1));
            Assert.That(hour.Routes.Keys, Is.EquivalentTo(new[] { "GET /a", "GET /b" }));
            // 10 minutes before the restart + live minutes 12:10 and 12:30.
            Assert.That(hour.CoveredMinutes, Is.EqualTo(12));
            // Building the snapshot must not change what is kept in memory.
            Assert.That(collector.SnapshotHours(hourStart).Single().Total.Count, Is.EqualTo(106));
        });
    }

    [Test]
    public void HourSnapshot_SplitsMinutesByHour()
    {
        var clock = new ManualTimeProvider(new DateTime(2026, 11, 20, 12, 59, 0, DateTimeKind.Utc));
        var collector = new MetricsCollector(clock);

        collector.RecordRequest("GET /a", 200, 1);
        clock.Advance(TimeSpan.FromMinutes(2));
        collector.RecordRequest("GET /a", 200, 1);
        collector.RecordRequest("GET /a", 200, 1);

        var hours = collector.SnapshotHours(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));

        Assert.That(hours.Select(h => (h.StartUtc.Hour, h.Total.Count)),
            Is.EqualTo(new[] { (12, 1.0), (13, 2.0) }));
    }
}

internal sealed class ManualTimeProvider(DateTime utcNow) : TimeProvider
{
    private DateTimeOffset _now = new(utcNow, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
