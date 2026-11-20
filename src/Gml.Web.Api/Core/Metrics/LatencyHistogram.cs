using System.Text.Json.Serialization;

namespace Gml.Web.Api.Core.Metrics;

/// <summary>
///     Response times in fixed bins, so minutes and hours can be summed and still give a p50/p95.
///     Counts are doubles because history hours are spread over chart points by weight.
/// </summary>
public sealed class LatencyHistogram
{
    /// <summary>Upper bound (inclusive) of each bin; one more bin catches everything slower.</summary>
    public static readonly double[] UpperBoundsMs =
    [
        1, 2, 3, 5, 7.5, 10, 15, 20, 30, 50, 75, 100, 150, 200, 300, 500, 750,
        1_000, 1_500, 2_000, 3_000, 5_000, 7_500, 10_000, 15_000, 30_000, 60_000
    ];

    private double[] _counts = new double[UpperBoundsMs.Length + 1];

    public double[] Counts
    {
        get => _counts;
        set
        {
            _counts = new double[UpperBoundsMs.Length + 1];
            Array.Copy(value, _counts, Math.Min(value.Length, _counts.Length));
        }
    }

    public double MaxMs { get; set; }

    [JsonIgnore]
    public double Total => _counts.Sum();

    public void Record(double ms)
    {
        if (ms < 0 || double.IsNaN(ms))
            ms = 0;

        _counts[BinOf(ms)]++;
        if (ms > MaxMs)
            MaxMs = ms;
    }

    public void Merge(LatencyHistogram other, double weight = 1)
    {
        for (var i = 0; i < _counts.Length; i++)
            _counts[i] += other._counts[i] * weight;

        if (weight > 0 && other.MaxMs > MaxMs)
            MaxMs = other.MaxMs;
    }

    /// <summary>Linear interpolation inside the bin that holds the rank, capped by the slowest request.</summary>
    public double? Percentile(double quantile)
    {
        var total = Total;
        if (total <= 0)
            return null;

        var rank = Math.Clamp(quantile, 0, 1) * total;
        double seen = 0;

        for (var i = 0; i < _counts.Length; i++)
        {
            var count = _counts[i];
            if (count <= 0)
                continue;

            if (seen + count >= rank)
            {
                // The overflow bin has no upper bound to interpolate to: report the slowest request.
                if (i == UpperBoundsMs.Length)
                    return MaxMs;

                var lower = i == 0 ? 0 : UpperBoundsMs[i - 1];
                var upper = UpperBoundsMs[i];
                var value = lower + (upper - lower) * ((rank - seen) / count);
                return Math.Min(value, MaxMs);
            }

            seen += count;
        }

        return MaxMs;
    }

    public LatencyHistogram Clone()
    {
        var copy = new LatencyHistogram { MaxMs = MaxMs };
        Array.Copy(_counts, copy._counts, _counts.Length);
        return copy;
    }

    private static int BinOf(double ms)
    {
        var index = Array.BinarySearch(UpperBoundsMs, ms);
        return index >= 0 ? index : ~index;
    }
}
