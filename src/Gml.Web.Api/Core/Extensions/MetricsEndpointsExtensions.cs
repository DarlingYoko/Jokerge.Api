using System.Net;
using Gml.Core.Services.Storage;
using Gml.Dto.Messages;
using Gml.Web.Api.Core.Handlers;
using Gml.Web.Api.Core.Metrics;
using GmlCore.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gml.Web.Api.Core.Extensions;

/// <summary>In-process API metrics (no Prometheus): middleware, 15 s sampler, hourly history, panel endpoint.</summary>
public static class MetricsEndpointsExtensions
{
    private const string Tag = "Metrics";
    private const string MetricsFileName = "metrics.db";

    public static IServiceCollection AddPanelMetrics(this IServiceCollection services, IConfiguration configuration)
    {
        var panelDatabase = DataSourceOf(configuration.GetConnectionString("SQLite")) ?? "database/data.db";
        var metricsDatabase = Path.Combine(Path.GetDirectoryName(panelDatabase) ?? string.Empty, MetricsFileName);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<MetricsCollector>();
        services.AddSingleton<ProcessMetrics>();
        services.AddSingleton(_ => new MetricsHourStore(metricsDatabase));
        services.AddSingleton(new MetricsDatabaseFile("База панели", panelDatabase));
        services.AddSingleton(provider => new MetricsDatabaseFile("База ядра (пользователи, ошибки)",
            Path.Combine(provider.GetRequiredService<IGmlManager>().LauncherInfo.Settings.InstallationDirectory,
                SqliteStorageService.DatabaseFileName)));
        services.AddSingleton(new MetricsDatabaseFile("История метрик", metricsDatabase));
        services.AddSingleton<MetricsLiveProbe>();
        services.AddHostedService<MetricsBackgroundService>();

        return services;
    }

    public static WebApplication MapMetricsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/metrics", MetricsHandler.GetReport)
            .WithDescription("Метрики API за период (1h, 6h, 24h): запросы, латентность, ошибки, эндпоинты, процесс")
            .WithName("API metrics")
            .WithTags(Tag)
            .Produces<ResponseMessage<MetricsReportDto>>()
            .Produces<ResponseMessage>((int)HttpStatusCode.BadRequest)
            .RequireAuthorization("perm:metrics.view");

        return app;
    }

    private static string? DataSourceOf(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        return string.IsNullOrWhiteSpace(dataSource) ? null : dataSource;
    }
}
