using System.Diagnostics;

namespace Gml.Web.Api.Core.Metrics;

/// <summary>CPU of this process between two samples, and the size of the SQLite files.</summary>
public sealed class ProcessMetrics
{
    private readonly object _lock = new();
    private TimeSpan _lastCpu;
    private long _lastTimestamp;

    public ProcessMetrics()
    {
        _lastCpu = CurrentCpuTime();
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>CPU used since the previous call, as a share of all cores (0–100).</summary>
    public double SampleCpuPercent()
    {
        lock (_lock)
        {
            var cpu = CurrentCpuTime();
            var now = Stopwatch.GetTimestamp();
            var percent = CpuPercent(cpu - _lastCpu, Stopwatch.GetElapsedTime(_lastTimestamp, now),
                Environment.ProcessorCount);

            _lastCpu = cpu;
            _lastTimestamp = now;
            return percent;
        }
    }

    public static double CpuPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int processorCount)
    {
        if (wallDelta <= TimeSpan.Zero || processorCount <= 0)
            return 0;

        var percent = cpuDelta.TotalMilliseconds / (wallDelta.TotalMilliseconds * processorCount) * 100;
        return Math.Clamp(percent, 0, 100);
    }

    /// <summary>The database file plus its -wal journal, which holds recent writes until a checkpoint.</summary>
    public static MetricsDatabaseDto MeasureDatabase(string name, string path)
    {
        var file = new FileInfo(path);
        var wal = new FileInfo(path + "-wal");

        return new MetricsDatabaseDto
        {
            Name = name,
            FileName = file.Name,
            Bytes = file.Exists ? file.Length + (wal.Exists ? wal.Length : 0) : null
        };
    }

    public static DateTime ProcessStartUtc()
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }

    private static TimeSpan CurrentCpuTime()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime;
    }
}
