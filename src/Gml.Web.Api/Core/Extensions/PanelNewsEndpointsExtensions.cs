using System.Net;
using Gml.Dto.Messages;
using Gml.Dto.News;
using Gml.Web.Api.Core.Handlers;

namespace Gml.Web.Api.Core.Extensions;

/// <summary>Routes for news the admin writes in the panel. The launcher reads them via /integrations/news/list.</summary>
public static class PanelNewsEndpointsExtensions
{
    private const string Tag = "News";

    public static WebApplication MapPanelNewsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/news", PanelNewsHandler.GetAll)
            .WithDescription("Список новостей панели (Markdown), новые сверху")
            .WithName("Panel news list")
            .WithTags(Tag)
            .Produces<ResponseMessage<List<PanelNewsReadDto>>>()
            .RequireAuthorization("perm:news.view");

        app.MapPost("/api/v1/news", PanelNewsHandler.Create)
            .WithDescription("Создание новости панели")
            .WithName("Create panel news")
            .WithTags(Tag)
            .Produces<ResponseMessage<PanelNewsReadDto>>((int)HttpStatusCode.Created)
            .Produces<ResponseMessage>((int)HttpStatusCode.BadRequest)
            .RequireAuthorization("perm:news.manage");

        app.MapPut("/api/v1/news/{id:int}", PanelNewsHandler.Update)
            .WithDescription("Изменение новости панели")
            .WithName("Update panel news")
            .WithTags(Tag)
            .Produces<ResponseMessage<PanelNewsReadDto>>()
            .Produces<ResponseMessage>((int)HttpStatusCode.BadRequest)
            .Produces<ResponseMessage>((int)HttpStatusCode.NotFound)
            .RequireAuthorization("perm:news.manage");

        app.MapDelete("/api/v1/news/{id:int}", PanelNewsHandler.Delete)
            .WithDescription("Удаление новости панели")
            .WithName("Delete panel news")
            .WithTags(Tag)
            .Produces<ResponseMessage>()
            .Produces<ResponseMessage>((int)HttpStatusCode.NotFound)
            .RequireAuthorization("perm:news.manage");

        return app;
    }
}
