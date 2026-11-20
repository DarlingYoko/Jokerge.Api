namespace Gml.Web.Api.Core.Metrics;

public enum MetricsPeriod
{
    Hour,
    SixHours,
    Day
}

/// <summary>Query codes and chart steps: every period comes out as 60–96 points.</summary>
public static class MetricsPeriods
{
    public static bool TryParse(string? code, out MetricsPeriod period)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case "1h":
                period = MetricsPeriod.Hour;
                return true;
            case "6h":
                period = MetricsPeriod.SixHours;
                return true;
            case "24h":
                period = MetricsPeriod.Day;
                return true;
            default:
                period = default;
                return false;
        }
    }

    public static string Code(MetricsPeriod period) => period switch
    {
        MetricsPeriod.Hour => "1h",
        MetricsPeriod.SixHours => "6h",
        _ => "24h"
    };

    public static int LengthMinutes(MetricsPeriod period) => period switch
    {
        MetricsPeriod.Hour => 60,
        MetricsPeriod.SixHours => 6 * 60,
        _ => 24 * 60
    };

    public static int StepMinutes(MetricsPeriod period) => period switch
    {
        MetricsPeriod.Hour => 1,
        MetricsPeriod.SixHours => 5,
        _ => 15
    };
}
