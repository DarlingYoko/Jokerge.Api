using GmlCore.Interfaces.Servers;
using System.Net;
using AutoMapper;
using FluentValidation;
using Gml.Dto.Messages;
using Gml.Dto.Servers;
using Gml.Models.Servers;
using Gml.Web.Api.Domains.Servers;
using GmlCore.Interfaces;

namespace Gml.Web.Api.Core.Handlers;

internal abstract class ServersHandler
{
    public static async Task<IResult> GetServers(IGmlManager gmlManager, IMapper mapper, string profileName)
    {
        if (string.IsNullOrEmpty(profileName))
            return Results.BadRequest(ResponseMessage.Create("Передан пустой параметр в качестве наименования профиля",
                HttpStatusCode.BadRequest));

        var profile = await gmlManager.Profiles.GetProfile(profileName);

        if (profile is null)
            return Results.BadRequest(ResponseMessage.Create("Профиль с данным именем не существует",
                HttpStatusCode.BadRequest));

        return Results.Ok(ResponseMessage.Create(mapper.Map<List<ServerReadDto>>(profile.Servers), string.Empty,
            HttpStatusCode.OK));
    }

    public static async Task<IResult> RemoveServer(IGmlManager gmlManager, string profileName, string serverNamesString)
    {
        if (string.IsNullOrEmpty(profileName))
            return Results.BadRequest(ResponseMessage.Create("Передан пустой параметр в качестве наименования профиля",
                HttpStatusCode.BadRequest));

        if (string.IsNullOrEmpty(serverNamesString))
            return Results.BadRequest(ResponseMessage.Create("Передан пустой параметр в качестве наименования сервера",
                HttpStatusCode.BadRequest));

        var profile = await gmlManager.Profiles.GetProfile(profileName);

        if (profile is null)
            return Results.BadRequest(ResponseMessage.Create("Профиль с данным именем не существует",
                HttpStatusCode.BadRequest));

        var servers = FindServersToRemove(profile.Servers, serverNamesString);

        // A 200 with nothing removed made the dashboard report success while the server stayed.
        if (servers.Count == 0)
            return Results.NotFound(ResponseMessage.Create($"Сервер \"{serverNamesString}\" не найден в профиле",
                HttpStatusCode.NotFound));

        foreach (var server in servers)
            profile.RemoveServer(server);

        await gmlManager.Profiles.SaveProfiles();

        return Results.Ok(ResponseMessage.Create($"Обработка завершена, всего удалено: {servers.Count}",
            HttpStatusCode.OK));
    }

    /// <summary>
    ///     Resolves the servers named in a delete request: an exact match on the whole string wins (names may contain
    ///     commas), otherwise it is a comma-separated list. All servers with a matching name are returned, and a name
    ///     that only matches after trimming still matches, since browsers drop trailing spaces from URLs.
    /// </summary>
    public static List<IProfileServer> FindServersToRemove(IEnumerable<IProfileServer> servers,
        string serverNamesString)
    {
        var all = servers.ToList();

        var names = all.Any(c => c.Name == serverNamesString)
            ? [serverNamesString]
            : serverNamesString.Split(',');

        return all
            .Where(server => names.Any(name =>
                server.Name == name || server.Name?.Trim() == name.Trim()))
            .ToList();
    }

    public static async Task<IResult> CreateServer(
        IGmlManager gmlManager,
        IValidator<CreateServerDto> validator,
        IMapper mapper,
        string profileName,
        CreateServerDto createDto)
    {
        try
        {
            if (string.IsNullOrEmpty(profileName))
                return Results.BadRequest(ResponseMessage.Create(
                    "Передан пустой параметр в качестве наименования профиля",
                    HttpStatusCode.BadRequest));

            var result = await validator.ValidateAsync(createDto);

            if (!result.IsValid)
                return Results.BadRequest(ResponseMessage.Create(result.Errors, "Ошибка валидации",
                    HttpStatusCode.BadRequest));

            var profile = await gmlManager.Profiles.GetProfile(profileName);

            if (profile is null)
                return Results.BadRequest(ResponseMessage.Create("Профиль с данным именем не существует",
                    HttpStatusCode.BadRequest));

            var mappedServer = mapper.Map<MinecraftServer>(createDto);
            mappedServer.ServerProcedures = gmlManager.Servers;

            profile.AddServer(mappedServer);

            await gmlManager.Profiles.SaveProfiles();

            var resultObject = ResponseMessage.Create(mapper.Map<ServerReadDto>(mappedServer),
                "Сервер успешно добавлен",
                HttpStatusCode.Created);

            return Results.Created($"/api/v1/servers/{profileName}/{mappedServer.Name}", resultObject);
        }
        catch (Exception exception)
        {
            return Results.BadRequest(ResponseMessage.Create(exception.Message, HttpStatusCode.BadRequest));
        }
    }

    public static async Task<IResult> UpdateServer(
        IGmlManager gmlManager,
        IValidator<UpdateServerDto> validator,
        IMapper mapper,
        string profileName,
        string serverName,
        UpdateServerDto updateDto)
    {
        try
        {
            if (string.IsNullOrEmpty(profileName))
                return Results.BadRequest(ResponseMessage.Create(
                    "Передан пустой параметр в качестве наименования профиля",
                    HttpStatusCode.BadRequest));

            if (string.IsNullOrEmpty(serverName))
                return Results.BadRequest(ResponseMessage.Create(
                    "Передан пустой параметр в качестве наименования сервера",
                    HttpStatusCode.BadRequest));

            var result = await validator.ValidateAsync(updateDto);

            if (!result.IsValid)
                return Results.BadRequest(ResponseMessage.Create(result.Errors, "Ошибка валидации",
                    HttpStatusCode.BadRequest));

            var profile = await gmlManager.Profiles.GetProfile(profileName);

            if (profile is null)
                return Results.BadRequest(ResponseMessage.Create("Профиль с данным именем не существует",
                    HttpStatusCode.BadRequest));

            var server = profile.Servers.FirstOrDefault(c => c.Name == serverName);

            if (server is null)
                return Results.BadRequest(ResponseMessage.Create("Сервер с данным именем не существует",
                    HttpStatusCode.BadRequest));

            server.Name = updateDto.Name;
            server.Address = updateDto.Address;
            server.Port = updateDto.Port;

            await server.UpdateStatusAsync();

            await gmlManager.Profiles.SaveProfiles();

            var resultObject = ResponseMessage.Create(mapper.Map<ServerReadDto>(server), "Сервер успешно обновлен",
                HttpStatusCode.OK);

            return Results.Ok(resultObject);
        }
        catch (Exception exception)
        {
            return Results.BadRequest(ResponseMessage.Create(exception.Message, HttpStatusCode.BadRequest));
        }
    }
}
