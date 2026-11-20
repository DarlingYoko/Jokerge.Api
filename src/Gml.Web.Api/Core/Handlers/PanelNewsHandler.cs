using System.Net;
using AutoMapper;
using FluentValidation;
using Gml.Dto.Messages;
using Gml.Dto.News;
using GmlCore.Interfaces;
using GmlCore.Interfaces.News;

namespace Gml.Web.Api.Core.Handlers;

/// <summary>CRUD for news the admin writes in the panel.</summary>
public static class PanelNewsHandler
{
    public static async Task<IResult> GetAll(IGmlManager gmlManager, IMapper mapper)
    {
        var news = await gmlManager.News.GetAllAsync();

        return Results.Ok(ResponseMessage.Create(mapper.Map<List<PanelNewsReadDto>>(news), "Новости панели",
            HttpStatusCode.OK));
    }

    public static async Task<IResult> Create(IGmlManager gmlManager, IMapper mapper,
        IValidator<PanelNewsWriteDto> validator, PanelNewsWriteDto dto)
    {
        var validation = await validator.ValidateAsync(dto);
        if (!validation.IsValid)
            return Results.BadRequest(ResponseMessage.Create(validation.Errors, "Ошибка валидации",
                HttpStatusCode.BadRequest));

        var created = await gmlManager.News.CreateAsync(ToDraft(dto));

        return Results.Created($"/api/v1/news/{created.Id}",
            ResponseMessage.Create(mapper.Map<PanelNewsReadDto>(created), "Новость создана",
                HttpStatusCode.Created));
    }

    public static async Task<IResult> Update(IGmlManager gmlManager, IMapper mapper,
        IValidator<PanelNewsWriteDto> validator, int id, PanelNewsWriteDto dto)
    {
        var validation = await validator.ValidateAsync(dto);
        if (!validation.IsValid)
            return Results.BadRequest(ResponseMessage.Create(validation.Errors, "Ошибка валидации",
                HttpStatusCode.BadRequest));

        var updated = await gmlManager.News.UpdateAsync(id, ToDraft(dto));

        return updated is null
            ? NotFound()
            : Results.Ok(ResponseMessage.Create(mapper.Map<PanelNewsReadDto>(updated), "Новость обновлена",
                HttpStatusCode.OK));
    }

    public static async Task<IResult> Delete(IGmlManager gmlManager, int id)
    {
        return await gmlManager.News.DeleteAsync(id)
            ? Results.Ok(ResponseMessage.Create("Новость удалена", HttpStatusCode.OK))
            : NotFound();
    }

    private static IResult NotFound()
    {
        return Results.NotFound(ResponseMessage.Create("Новость не найдена", HttpStatusCode.NotFound));
    }

    private static PanelNewsDraft ToDraft(PanelNewsWriteDto dto)
    {
        return new PanelNewsDraft
        {
            Title = dto.Title,
            Content = dto.Content,
            ProfileName = dto.ProfileName,
            BuildVersion = dto.BuildVersion,
            Date = dto.Date ?? DateTimeOffset.UtcNow
        };
    }
}
