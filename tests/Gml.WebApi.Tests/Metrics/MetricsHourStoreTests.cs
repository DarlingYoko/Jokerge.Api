using Gml.Web.Api.Core.Metrics;

namespace Gml.WebApi.Tests.Metrics;

public class MetricsHourStoreTests
{
    private static readonly DateTime Hour = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    private string _directory = null!;
    private MetricsHourStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "gml-metrics-" + Guid.NewGuid().ToString("N"));
        _store = new MetricsHourStore(Path.Combine(_directory, "metrics.db"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private static MetricsBucket HourWith(DateTime start, int requests)
    {
        var bucket = MetricsBucket.Create(start, 60);
        for (var i = 0; i < requests; i++) bucket.RecordRequest("GET /api/v1/profiles", i == 0 ? 500 : 200, 40);
        bucket.RecordSample(3, 25, 4096);
        bucket.CoveredMinutes = 42;
        return bucket;
    }

    [Test]
    public void SavedHour_ComesBackTheSame()
    {
        _store.Save([HourWith(Hour, 10)]);

        var loaded = _store.Load(Hour.AddHours(-1)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.StartUtc, Is.EqualTo(Hour));
            Assert.That(loaded.StartUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(loaded.Minutes, Is.EqualTo(60));
            Assert.That(loaded.Total.Count, Is.EqualTo(10));
            Assert.That(loaded.Total.ServerErrors, Is.EqualTo(1));
            Assert.That(loaded.Total.Latency.Percentile(0.5), Is.EqualTo(40).Within(0.1));
            Assert.That(loaded.Routes["GET /api/v1/profiles"].Count, Is.EqualTo(10));
            Assert.That(loaded.LaunchersMax, Is.EqualTo(3));
            Assert.That(loaded.CpuPercentAvg, Is.EqualTo(25));
            Assert.That(loaded.MemoryBytesMax, Is.EqualTo(4096));
            Assert.That(loaded.CoveredMinutes, Is.EqualTo(42));
        });
    }

    [Test]
    public void SavingTheSameHourAgain_ReplacesIt()
    {
        _store.Save([HourWith(Hour, 10)]);
        _store.Save([HourWith(Hour, 25)]);

        Assert.That(_store.Load(Hour.AddHours(-1)).Single().Total.Count, Is.EqualTo(25));
    }

    [Test]
    public void Load_SkipsOlderHours_AndPruneDeletesThem()
    {
        _store.Save([HourWith(Hour.AddHours(-30), 1), HourWith(Hour.AddHours(-1), 2), HourWith(Hour, 3)]);

        var recent = _store.Load(Hour.AddHours(-24));
        var removed = _store.Prune(Hour.AddHours(-24));

        Assert.Multiple(() =>
        {
            Assert.That(recent.Select(h => h.Total.Count), Is.EqualTo(new[] { 2.0, 3.0 }));
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(_store.Load(DateTime.MinValue.ToUniversalTime()), Has.Count.EqualTo(2));
        });
    }
}
