using Gml.Web.Api.Core.Metrics;

namespace Gml.WebApi.Tests.Metrics;

public class LatencyHistogramTests
{
    [Test]
    public void Empty_HasNoPercentile()
    {
        var histogram = new LatencyHistogram();

        Assert.Multiple(() =>
        {
            Assert.That(histogram.Total, Is.EqualTo(0));
            Assert.That(histogram.Percentile(0.5), Is.Null);
        });
    }

    [Test]
    public void UniformValues_PercentilesLandOnTheRightValue()
    {
        var histogram = new LatencyHistogram();
        for (var ms = 1; ms <= 100; ms++)
            histogram.Record(ms);

        Assert.Multiple(() =>
        {
            Assert.That(histogram.Total, Is.EqualTo(100));
            Assert.That(histogram.Percentile(0.5), Is.EqualTo(50).Within(0.001));
            Assert.That(histogram.Percentile(0.95), Is.EqualTo(95).Within(0.001));
            Assert.That(histogram.MaxMs, Is.EqualTo(100));
        });
    }

    [Test]
    public void Percentile_NeverExceedsTheSlowestRequest()
    {
        var histogram = new LatencyHistogram();
        for (var i = 0; i < 10; i++)
            histogram.Record(120);

        Assert.That(histogram.Percentile(0.5), Is.EqualTo(120));
    }

    [Test]
    public void VerySlowRequests_GoToTheOverflowBin()
    {
        var histogram = new LatencyHistogram();
        histogram.Record(90_000);

        Assert.Multiple(() =>
        {
            Assert.That(histogram.Counts[^1], Is.EqualTo(1));
            Assert.That(histogram.Percentile(0.95), Is.EqualTo(90_000));
        });
    }

    [Test]
    public void Merge_SumsCountsAndKeepsTheMax()
    {
        var fast = new LatencyHistogram();
        var slow = new LatencyHistogram();
        for (var i = 0; i < 90; i++) fast.Record(10);
        for (var i = 0; i < 10; i++) slow.Record(1000);

        fast.Merge(slow);

        Assert.Multiple(() =>
        {
            Assert.That(fast.Total, Is.EqualTo(100));
            Assert.That(fast.MaxMs, Is.EqualTo(1000));
            Assert.That(fast.Percentile(0.5), Is.LessThanOrEqualTo(10));
            Assert.That(fast.Percentile(0.95), Is.GreaterThan(750));
        });
    }

    [Test]
    public void Merge_WithWeight_ScalesCounts()
    {
        var source = new LatencyHistogram();
        for (var i = 0; i < 60; i++) source.Record(40);

        var target = new LatencyHistogram();
        target.Merge(source, 0.25);

        Assert.That(target.Total, Is.EqualTo(15).Within(0.0001));
    }

    [Test]
    public void Clone_IsIndependent()
    {
        var source = new LatencyHistogram();
        source.Record(5);

        var copy = source.Clone();
        copy.Record(5);

        Assert.That(source.Total, Is.EqualTo(1));
    }
}
