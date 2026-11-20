using Gml.Web.Api.Core.Metrics;

namespace Gml.WebApi.Tests.Metrics;

public class MetricsRingTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void SameMinute_GoesToOneBucket()
    {
        var ring = new MetricsRing();

        ring.GetOrCreate(T0.AddSeconds(1)).RecordRequest("GET /a", 200, 10);
        ring.GetOrCreate(T0.AddSeconds(59)).RecordRequest("GET /a", 500, 30);

        var buckets = ring.Range(T0, T0.AddMinutes(1)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(buckets, Has.Count.EqualTo(1));
            Assert.That(buckets[0].StartUtc, Is.EqualTo(T0));
            Assert.That(buckets[0].Minutes, Is.EqualTo(1));
            Assert.That(buckets[0].Total.Count, Is.EqualTo(2));
            Assert.That(buckets[0].Total.ServerErrors, Is.EqualTo(1));
            Assert.That(buckets[0].Routes["GET /a"].Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void StatusClasses_AreCountedSeparately()
    {
        var bucket = new MetricsRing().GetOrCreate(T0);

        bucket.RecordRequest("GET /a", 200, 1);
        bucket.RecordRequest("GET /a", 302, 1);
        bucket.RecordRequest("GET /a", 404, 1);
        bucket.RecordRequest("GET /a", 429, 1);
        bucket.RecordRequest("GET /a", 503, 1);

        Assert.Multiple(() =>
        {
            Assert.That(bucket.Total.Count, Is.EqualTo(5));
            Assert.That(bucket.Total.ClientErrors, Is.EqualTo(2));
            Assert.That(bucket.Total.ServerErrors, Is.EqualTo(1));
        });
    }

    [Test]
    public void Range_ReturnsOnlyBucketsInsideTheWindow()
    {
        var ring = new MetricsRing();
        for (var minute = 0; minute < 10; minute++)
            ring.GetOrCreate(T0.AddMinutes(minute)).RecordRequest("GET /a", 200, 1);

        var buckets = ring.Range(T0.AddMinutes(3), T0.AddMinutes(6)).ToList();

        Assert.That(buckets.Select(b => b.StartUtc),
            Is.EqualTo(new[] { T0.AddMinutes(3), T0.AddMinutes(4), T0.AddMinutes(5) }));
    }

    [Test]
    public void SlotReuse_DropsDataOlderThanTheCapacity()
    {
        var ring = new MetricsRing(capacityMinutes: 60);

        ring.GetOrCreate(T0).RecordRequest("GET /old", 200, 1);
        ring.GetOrCreate(T0.AddMinutes(60)).RecordRequest("GET /new", 200, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ring.Range(T0, T0.AddMinutes(1)), Is.Empty);
            var current = ring.Range(T0.AddMinutes(60), T0.AddMinutes(61)).Single();
            Assert.That(current.Routes.Keys, Is.EquivalentTo(new[] { "GET /new" }));
            Assert.That(current.Total.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void TooManyRoutes_OverflowIntoOneRow()
    {
        var bucket = new MetricsRing().GetOrCreate(T0);

        for (var i = 0; i < MetricsBucket.MaxRoutes + 5; i++)
            bucket.RecordRequest($"GET /r{i}", 200, 1);

        Assert.Multiple(() =>
        {
            Assert.That(bucket.Routes, Has.Count.EqualTo(MetricsBucket.MaxRoutes + 1));
            Assert.That(bucket.Routes[MetricsBucket.OtherRoutes].Count, Is.EqualTo(5));
            Assert.That(bucket.Total.Count, Is.EqualTo(MetricsBucket.MaxRoutes + 5));
        });
    }

    [Test]
    public void Samples_KeepPeakLaunchersAverageCpuAndPeakMemory()
    {
        var bucket = new MetricsRing().GetOrCreate(T0);

        bucket.RecordSample(3, 10, 100);
        bucket.RecordSample(5, 30, 300);
        bucket.RecordSample(4, 20, 200);

        Assert.Multiple(() =>
        {
            Assert.That(bucket.LaunchersMax, Is.EqualTo(5));
            Assert.That(bucket.CpuPercentAvg, Is.EqualTo(20).Within(0.0001));
            Assert.That(bucket.MemoryBytesMax, Is.EqualTo(300));
        });
    }

    [Test]
    public void BucketMerge_CombinesRequestsRoutesAndSamples()
    {
        var ring = new MetricsRing();
        var first = ring.GetOrCreate(T0);
        first.RecordRequest("GET /a", 200, 10);
        first.RecordSample(2, 10, 100);
        var second = ring.GetOrCreate(T0.AddMinutes(1));
        second.RecordRequest("GET /b", 500, 10);
        second.RecordSample(4, 30, 50);

        var hour = MetricsBucket.Create(T0, 60);
        hour.Merge(first);
        hour.Merge(second);

        Assert.Multiple(() =>
        {
            Assert.That(hour.Total.Count, Is.EqualTo(2));
            Assert.That(hour.Routes.Keys, Is.EquivalentTo(new[] { "GET /a", "GET /b" }));
            Assert.That(hour.LaunchersMax, Is.EqualTo(4));
            Assert.That(hour.CpuPercentAvg, Is.EqualTo(20).Within(0.0001));
            Assert.That(hour.MemoryBytesMax, Is.EqualTo(100));
            Assert.That(hour.CoveredMinutes, Is.EqualTo(2));
        });
    }
}
