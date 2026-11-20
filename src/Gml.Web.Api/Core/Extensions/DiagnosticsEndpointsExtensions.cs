using System.Net;
using Gml.Core.Services.Storage;
using Gml.Dto.Messages;
using Gml.Web.Api.Core.Diagnostics;
using Gml.Web.Api.Core.Handlers;
using GmlCore.Interfaces;
using GmlCore.Interfaces.Storage;

namespace Gml.Web.Api.Core.Extensions;

/// <summary>Diagnostics reports sent from the launcher settings and shown in the panel.</summary>
public static class DiagnosticsEndpointsExtensions
{
    private const string Tag = "Diagnostics";

    /// <summary>Reports live in the Core SQLite file next to users and bugs.</summary>
    public static IServiceCollection AddDiagnostics(this IServiceCollection services)
    {
        services.AddSingleton<IDiagnosticReportStorage>(provider =>
            new SqliteDiagnosticReportStorage(provider.GetRequiredService<IGmlManager>().LauncherInfo.Settings));
        services.AddSingleton<DiagnosticReportService>();

        return services;
    }

    public static WebApplication MapDiagnosticsEndpoints(this WebApplication app)
    {
        // Authorised by the player's access token inside the handler, like /profiles/info.
        app.MapPost("/api/v1/launcher/diagnostics", DiagnosticsHandler.Submit)
            .WithDescription("Отправка отчёта диагностики из лаунчера (токен игрока в Authorization)")
            .WithName("Submit launcher diagnostics")
            .WithTags(Tag)
            .Produces<ResponseMessage<DiagnosticReportCreatedDto>>()
            .Produces<ResponseMessage>((int)HttpStatusCode.BadRequest)
            .Produces((int)HttpStatusCode.Forbidden)
            .RequireRateLimiting(RateLimitExtension.AuthPolicy);

        app.MapGet("/api/v1/diagnostics", DiagnosticsHandler.GetPage)
            .WithDescription("Список отчётов диагностики, новые сверху; userName — фильтр по нику")
            .WithName("Diagnostics reports list")
            .WithTags(Tag)
            .Produces<ResponseMessage<DiagnosticReportPageDto>>()
            .RequireAuthorization("perm:diagnostics.view");

        app.MapGet("/api/v1/diagnostics/{id:guid}", DiagnosticsHandler.GetById)
            .WithDescription("Отчёт диагностики целиком")
            .WithName("Diagnostics report")
            .WithTags(Tag)
            .Produces<ResponseMessage<DiagnosticReportReadDto>>()
            .Produces<ResponseMessage>((int)HttpStatusCode.NotFound)
            .RequireAuthorization("perm:diagnostics.view");

        app.MapDelete("/api/v1/diagnostics/{id:guid}", DiagnosticsHandler.Remove)
            .WithDescription("Удаление отчёта диагностики")
            .WithName("Delete diagnostics report")
            .WithTags(Tag)
            .Produces<ResponseMessage>()
            .Produces<ResponseMessage>((int)HttpStatusCode.NotFound)
            .RequireAuthorization("perm:diagnostics.delete");

        return app;
    }
}
