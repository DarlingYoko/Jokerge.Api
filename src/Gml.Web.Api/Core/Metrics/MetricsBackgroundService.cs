using Gml.Web.Api.Core.Hubs.Controllers;

namespace Gml.Web.Api.Core.Metrics;

/// <summary>
///     Samples launchers online / CPU / memory every 15 s and writes the current and previous hour to
///     <see cref="MetricsHourStore" /> every 5 min and on shutdown, so a restart keeps the 24 h history.
/// </summary>
public sealed class MetricsBackgroundService(
    MetricsCollector collector,
    MetricsHourStore store,
    ProcessMetrics process,
    PlayersController players,
    TimeProvider time,
    ILogger<MetricsBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PersistInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KeepHistory = TimeSpan.FromDays(7);

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RestoreHistory();
        Sample();

        var lastPersist = UtcNow;
        using var timer = new PeriodicTimer(SampleInterval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Sample();

                if (UtcNow - lastPersist < PersistInterval)
                    continue;

                Persist();
                lastPersist = UtcNow;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down; StopAsync persists.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        Persist();
    }

    private void RestoreHistory()
    {
        try
        {
            collector.Restore(store.Load(UtcNow.AddHours(-25)));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Metrics history could not be read from {Path}", store.DatabasePath);
        }
    }

    private void Sample()
    {
        try
        {
            collector.RecordSample(players.LauncherInfos.Count, process.SampleCpuPercent(), Environment.WorkingSet);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Metrics sample failed");
        }
    }

    private void Persist()
    {
        try
        {
            var now = UtcNow;
            store.Save(collector.SnapshotHours(now.AddHours(-1)));
            store.Prune(now - KeepHistory);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Metrics history could not be written to {Path}", store.DatabasePath);
        }
    }
}
