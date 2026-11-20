using Gml.Web.Api.Core.Metrics;

namespace Gml.WebApi.Tests.Metrics;

public class MetricsReportBuilderTests
{
    // 30 s past the minute: the current minute is still filling up.
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 30, DateTimeKind.Utc);
    private static readonly DateTime CurrentMinute = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private static MetricsBucket Minute(DateTime start, Action<MetricsBucket> fill)
    {
        var bucket = MetricsBucket.Create(start, 1);
        fill(bucket);
        return bucket;
    }

    [TestCase("1h", MetricsPeriod.Hour, 60, 1)]
    [TestCase("6h", MetricsPeriod.SixHours, 72, 5)]
    [TestCase("24h", MetricsPeriod.Day, 96, 15)]
    public void Periods_HaveAFixedNumberOfPoints(string code, MetricsPeriod expected, int points, int step)
    {
        Assert.That(MetricsPeriods.TryParse(code, out var period), Is.True);

        var report = MetricsReportBuilder.Build([], Now, period);

        Assert.Multiple(() =>
        {
            Assert.That(period, Is.EqualTo(expected));
            Assert.That(report.Period, Is.EqualTo(code));
            Assert.That(report.StepMinutes, Is.EqualTo(step));
            Assert.That(report.Series, Has.Count.EqualTo(points));
            Assert.That(report.Series[^1].TimeUtc, Is.EqualTo(CurrentMinute));
            Assert.That(report.ToUtc, Is.EqualTo(Now));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("7d")]
    public void UnknownPeriod_IsRejected(string? code)
    {
        Assert.That(MetricsPeriods.TryParse(code, out _), Is.False);
    }

    [Test]
    public void NoData_GivesEmptyPointsInsteadOfZeros()
    {
        var report = MetricsReportBuilder.Build([], Now, MetricsPeriod.Hour);

        Assert.Multiple(() =>
        {
            Assert.That(report.Summary.Requests, Is.EqualTo(0));
            Assert.That(report.Summary.CoveredMinutes, Is.EqualTo(0));
            Assert.That(report.Summary.RequestsPerMinute, Is.Null);
            Assert.That(report.Summary.P95Ms, Is.Null);
            Assert.That(report.Summary.ServerErrorRate, Is.Null);
            Assert.That(report.Series.All(p => p.Requests is null), Is.True);
            Assert.That(report.Endpoints, Is.Empty);
        });
    }

    [Test]
    public void Summary_CountsRequestsErrorsAndRate()
    {
        var bucket = Minute(CurrentMinute.AddMinutes(-5), b =>
        {
            for (var i = 0; i < 16; i++) b.RecordRequest("GET /api/v1/profiles", 200, 100);
            for (var i = 0; i < 3; i++) b.RecordRequest("GET /api/v1/profiles", 404, 5);
            b.RecordRequest("POST /api/v1/users/signin", 500, 2000);
        });
        var idle = Minute(CurrentMinute.AddMinutes(-4), b => b.RecordSample(1, 5, 100));

        var report = MetricsReportBuilder.Build([bucket, idle], Now, MetricsPeriod.Hour);

        Assert.Multiple(() =>
        {
            Assert.That(report.Summary.Requests, Is.EqualTo(20));
            Assert.That(report.Summary.ClientErrors, Is.EqualTo(3));
            Assert.That(report.Summary.ServerErrors, Is.EqualTo(1));
            Assert.That(report.Summary.ClientErrorRate, Is.EqualTo(0.15).Within(0.0001));
            Assert.That(report.Summary.ServerErrorRate, Is.EqualTo(0.05).Within(0.0001));
            Assert.That(report.Summary.CoveredMinutes, Is.EqualTo(2));
            Assert.That(report.Summary.RequestsPerMinute, Is.EqualTo(10));
            // 3 fast 404s pull the median down inside the 75–100 ms bin.
            Assert.That(report.Summary.P50Ms, Is.InRange(75, 100));
            Assert.That(report.Summary.P95Ms, Is.EqualTo(100).Within(0.1));
        });
    }

    [Test]
    public void Series_HasValuesOnlyWhereTheApiWasUp()
    {
        var busy = Minute(CurrentMinute.AddMinutes(-5), b =>
        {
            b.RecordRequest("GET /a", 200, 10);
            b.RecordRequest("GET /a", 503, 10);
        });
        var idle = Minute(CurrentMinute.AddMinutes(-4), b => b.RecordSample(7, 12.5, 1024));

        var report = MetricsReportBuilder.Build([busy, idle], Now, MetricsPeriod.Hour);
        var byTime = report.Series.ToDictionary(p => p.TimeUtc);

        Assert.Multiple(() =>
        {
            Assert.That(byTime[CurrentMinute.AddMinutes(-5)].Requests, Is.EqualTo(2));
            Assert.That(byTime[CurrentMinute.AddMinutes(-5)].ServerErrors, Is.EqualTo(1));
            Assert.That(byTime[CurrentMinute.AddMinutes(-4)].Requests, Is.EqualTo(0));
            Assert.That(byTime[CurrentMinute.AddMinutes(-4)].LaunchersOnline, Is.EqualTo(7));
            Assert.That(byTime[CurrentMinute.AddMinutes(-4)].CpuPercent, Is.EqualTo(12.5));
            Assert.That(byTime[CurrentMinute.AddMinutes(-4)].MemoryBytes, Is.EqualTo(1024));
            Assert.That(byTime[CurrentMinute.AddMinutes(-3)].Requests, Is.Null);
        });
    }

    [Test]
    public void BucketsOutsideTheWindow_AreIgnored()
    {
        var old = Minute(CurrentMinute.AddMinutes(-60), b => b.RecordRequest("GET /a", 200, 1));
        var inside = Minute(CurrentMinute.AddMinutes(-59), b => b.RecordRequest("GET /a", 200, 1));

        var report = MetricsReportBuilder.Build([old, inside], Now, MetricsPeriod.Hour);

        Assert.Multiple(() =>
        {
            Assert.That(report.Summary.Requests, Is.EqualTo(1));
            Assert.That(report.FromUtc, Is.EqualTo(CurrentMinute.AddMinutes(-59)));
        });
    }

    [Test]
    public void Day_GroupsMinutesIntoQuarterHours()
    {
        var quarter = new DateTime(2026, 11, 20, 11, 30, 0, DateTimeKind.Utc);
        var buckets = Enumerable.Range(0, 15)
            .Select(i => Minute(quarter.AddMinutes(i), b =>
            {
                b.RecordRequest("GET /a", 200, 10);
                b.RecordSample(i, 10, i * 10);
            }))
            .ToList();

        var report = MetricsReportBuilder.Build(buckets, Now, MetricsPeriod.Day);
        var point = report.Series.Single(p => p.TimeUtc == quarter);

        Assert.Multiple(() =>
        {
            Assert.That(point.Requests, Is.EqualTo(15));
            Assert.That(point.RequestsPerMinute, Is.EqualTo(1));
            Assert.That(point.LaunchersOnline, Is.EqualTo(14));
            Assert.That(point.MemoryBytes, Is.EqualTo(140));
        });
    }

    [Test]
    public void HourFromHistory_IsSpreadOverItsQuarterHours()
    {
        var hourStart = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        var hour = MetricsBucket.Create(hourStart, 60);
        for (var i = 0; i < 600; i++) hour.RecordRequest("GET /a", 200, 50);
        hour.RecordSample(9, 40, 2048);

        var report = MetricsReportBuilder.Build([hour], Now, MetricsPeriod.Day);
        var points = report.Series.Where(p => p.TimeUtc >= hourStart && p.TimeUtc < hourStart.AddHours(1)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(points, Has.Count.EqualTo(4));
            Assert.That(points.Select(p => p.Requests), Is.All.EqualTo(150));
            Assert.That(points.Select(p => p.RequestsPerMinute), Is.All.EqualTo(10));
            Assert.That(points.Select(p => p.LaunchersOnline), Is.All.EqualTo(9));
            Assert.That(report.Summary.Requests, Is.EqualTo(600));
            Assert.That(report.Summary.CoveredMinutes, Is.EqualTo(60));
        });
    }

    [Test]
    public void HourCutByTheWindow_CountsOnlyTheOverlap()
    {
        // 1 h window starts at 11:01, so 11:00–12:00 history overlaps it by 59 minutes.
        var hour = MetricsBucket.Create(new DateTime(2026, 11, 20, 11, 0, 0, DateTimeKind.Utc), 60);
        for (var i = 0; i < 600; i++) hour.RecordRequest("GET /a", 200, 50);

        var report = MetricsReportBuilder.Build([hour], Now, MetricsPeriod.Hour);

        Assert.Multiple(() =>
        {
            Assert.That(report.Summary.Requests, Is.EqualTo(590));
            Assert.That(report.Summary.CoveredMinutes, Is.EqualTo(59));
        });
    }

    [Test]
    public void HistoryAndLiveMinutesOfTheSameHour_AddUpTheirOwnCoverage()
    {
        // The API ran 11:00–11:20, restarted, and has one live minute since.
        var hourStart = new DateTime(2026, 11, 20, 11, 0, 0, DateTimeKind.Utc);
        var history = MetricsBucket.Create(hourStart, 60);
        history.RecordRequest("GET /a", 200, 1);
        history.CoveredMinutes = 20;
        var live = Minute(hourStart.AddMinutes(30), b => b.RecordRequest("GET /a", 200, 1));

        var report = MetricsReportBuilder.Build([history, live], Now, MetricsPeriod.Day);

        Assert.That(report.Summary.CoveredMinutes, Is.EqualTo(21));
    }

    [Test]
    public void PartlyCoveredHistoryHour_GivesTheRealRequestRate()
    {
        // 120 requests while the API was up for 12 minutes of that hour: 10/min, not 2/min.
        var hourStart = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        var hour = MetricsBucket.Create(hourStart, 60);
        for (var i = 0; i < 120; i++) hour.RecordRequest("GET /a", 200, 50);
        hour.CoveredMinutes = 12;

        var report = MetricsReportBuilder.Build([hour], Now, MetricsPeriod.Day);
        var point = report.Series.Single(p => p.TimeUtc == hourStart);

        Assert.Multiple(() =>
        {
            Assert.That(report.Summary.CoveredMinutes, Is.EqualTo(12));
            Assert.That(report.Summary.RequestsPerMinute, Is.EqualTo(10));
            Assert.That(report.Endpoints.Single().RequestsPerMinute, Is.EqualTo(10));
            Assert.That(point.Requests, Is.EqualTo(30));
            Assert.That(point.RequestsPerMinute, Is.EqualTo(10));
        });
    }

    [Test]
    public void Endpoints_AreSortedByTrafficWithTheirOwnLatencyAndErrors()
    {
        var bucket = Minute(CurrentMinute.AddMinutes(-1), b =>
        {
            for (var i = 0; i < 10; i++) b.RecordRequest("GET /api/v1/profiles", 200, 20);
            for (var i = 0; i < 3; i++) b.RecordRequest("POST /api/v1/profiles/restore", 200, 5000);
            b.RecordRequest("POST /api/v1/profiles/restore", 500, 9000);
            for (var i = 0; i < 2; i++) b.RecordRequest("POST /api/v1/users/signin", 401, 30);
        });

        var report = MetricsReportBuilder.Build([bucket], Now, MetricsPeriod.Hour);
        var endpoints = report.Endpoints;

        Assert.Multiple(() =>
        {
            Assert.That(endpoints.Select(e => e.Route), Is.EqualTo(new[]
            {
                "GET /api/v1/profiles", "POST /api/v1/profiles/restore", "POST /api/v1/users/signin"
            }));
            Assert.That(endpoints[0].Requests, Is.EqualTo(10));
            Assert.That(endpoints[0].RequestsPerMinute, Is.EqualTo(10));
            Assert.That(endpoints[1].ServerErrors, Is.EqualTo(1));
            Assert.That(endpoints[1].MaxMs, Is.EqualTo(9000));
            Assert.That(endpoints[1].P95Ms, Is.GreaterThan(endpoints[0].P95Ms));
            Assert.That(endpoints[2].ClientErrors, Is.EqualTo(2));
        });
    }

    [Test]
    public void Endpoints_AreCapped()
    {
        var bucket = Minute(CurrentMinute, b =>
        {
            for (var i = 0; i < MetricsReportBuilder.MaxEndpoints + 10; i++)
                b.RecordRequest($"GET /r{i}", 200, 1);
        });

        var report = MetricsReportBuilder.Build([bucket], Now, MetricsPeriod.Hour);

        Assert.That(report.Endpoints, Has.Count.EqualTo(MetricsReportBuilder.MaxEndpoints));
    }
}
