namespace Gml.Web.Api.Core.Metrics;

/// <summary>Requests of one route (or all routes) in one bucket.</summary>
public sealed class RequestStats
{
    public double Count { get; set; }
    public double ClientErrors { get; set; }
    public double ServerErrors { get; set; }
    public LatencyHistogram Latency { get; set; } = new();

    public void Record(int statusCode, double ms)
    {
        Count++;
        if (statusCode is >= 400 and < 500)
            ClientErrors++;
        else if (statusCode >= 500)
            ServerErrors++;

        Latency.Record(ms);
    }

    public void Merge(RequestStats other, double weight = 1)
    {
        Count += other.Count * weight;
        ClientErrors += other.ClientErrors * weight;
        ServerErrors += other.ServerErrors * weight;
        Latency.Merge(other.Latency, weight);
    }
}
