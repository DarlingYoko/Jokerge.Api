using Gml.Web.Api.Core.Metrics;

namespace Gml.WebApi.Tests.Metrics;

public class ProcessMetricsTests
{
    [Test]
    public void CpuPercent_IsShareOfAllCores()
    {
        // 2 s of CPU in 1 s of wall time on 4 cores = half the machine.
        var percent = ProcessMetrics.CpuPercent(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 4);

        Assert.That(percent, Is.EqualTo(50).Within(0.0001));
    }

    [Test]
    public void CpuPercent_StaysWithinZeroAndHundred()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessMetrics.CpuPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 4), Is.EqualTo(0));
            Assert.That(ProcessMetrics.CpuPercent(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1), 4),
                Is.EqualTo(0));
            Assert.That(ProcessMetrics.CpuPercent(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(1), 4),
                Is.EqualTo(100));
        });
    }

    [Test]
    public void DatabaseSize_IncludesTheWriteAheadLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gml-dbsize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "data.db");
            File.WriteAllBytes(path, new byte[1000]);
            File.WriteAllBytes(path + "-wal", new byte[24]);

            var size = ProcessMetrics.MeasureDatabase("Панель", path);
            var missing = ProcessMetrics.MeasureDatabase("Нет", Path.Combine(directory, "none.db"));

            Assert.Multiple(() =>
            {
                Assert.That(size.Name, Is.EqualTo("Панель"));
                Assert.That(size.FileName, Is.EqualTo("data.db"));
                Assert.That(size.Bytes, Is.EqualTo(1024));
                Assert.That(missing.Bytes, Is.Null);
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
