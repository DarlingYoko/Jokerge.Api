using System.Net;
using Gml.Dto.Messages;
using Gml.Web.Api.Core.Diagnostics;
using GmlCore.Interfaces;

namespace Gml.Web.Api.Core.Handlers;

/// <summary>Diagnostics reports: posted by the launcher, read in the panel.</summary>
public static class DiagnosticsHandler
{
    public static async Task<IResult> Submit(
        HttpContext context,
        IGmlManager gmlManager,
        DiagnosticReportService service,
        DiagnosticReportSubmitDto? request)
    {
        if (request?.Report is null || string.IsNullOrWhiteSpace(request.UserName))
            return Results.BadRequest(ResponseMessage.Create("Пустой отчёт диагностики", HttpStatusCode.BadRequest));

        var user = await gmlManager.Users.GetUserByName(request.UserName);

        if (!DiagnosticReportService.IsAuthorized(user, context.Request.Headers.Authorization.FirstOrDefault()))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var id = await service.SubmitAsync(user!, request.Report);

        return Results.Ok(ResponseMessage.Create(new DiagnosticReportCreatedDto { Id = id },
            "Отчёт диагностики сохранён", HttpStatusCode.OK));
    }

    public static async Task<IResult> GetPage(
        DiagnosticReportService service,
        int? take,
        int? offset,
        string? userName)
    {
        var page = await service.GetPageAsync(take ?? 20, offset ?? 0, userName);

        return Results.Ok(ResponseMessage.Create(page, "Список отчётов диагностики", HttpStatusCode.OK));
    }

    public static async Task<IResult> GetById(DiagnosticReportService service, Guid id)
    {
        var report = await service.GetAsync(id);

        return report is null
            ? Results.NotFound(ResponseMessage.Create("Отчёт не найден", HttpStatusCode.NotFound))
            : Results.Ok(ResponseMessage.Create(report, "Отчёт диагностики", HttpStatusCode.OK));
    }

    public static async Task<IResult> Remove(DiagnosticReportService service, Guid id)
    {
        return await service.RemoveAsync(id)
            ? Results.Ok(ResponseMessage.Create("Отчёт удалён", HttpStatusCode.OK))
            : Results.NotFound(ResponseMessage.Create("Отчёт не найден", HttpStatusCode.NotFound));
    }
}
