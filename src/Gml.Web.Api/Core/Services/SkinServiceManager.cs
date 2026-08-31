using System.Net.Http.Headers;
using Gml.Common.TextureService;
using Gml.Models.User;
using Gml.Web.Api.Core.Options;
using Newtonsoft.Json;

namespace Gml.Web.Api.Core.Services;

public class SkinServiceManager(IHttpClientFactory httpClientFactory) : ISkinServiceManager
{
    private HttpClient _skinServiceClient = httpClientFactory.CreateClient(HttpClientNames.SkinService);

    public async Task<string?> UpdateSkin(AuthUser authUser, Stream texture)
    {
        var content = new MultipartFormDataContent();

        content.Add(new StreamContent(texture)
        {
            Headers =
            {
                ContentLength = texture.Length,
                ContentType = new MediaTypeHeaderValue("image/png")
            }
        }, "file", "skin.png"); // Pass the name of the form field, the file name, and the content type

        var request = await _skinServiceClient.PostAsync($"/skin/{authUser.Name}", content);

        if (!request.IsSuccessStatusCode)
            return null;

        var data = await request.Content.ReadAsStringAsync();

        return JsonConvert.DeserializeObject<TextureReadDto>(data)?.SkinUrl;
    }

    public async Task<string?> UpdateCloak(AuthUser authUser, Stream texture)
    {
        var content = new MultipartFormDataContent();

        content.Add(new StreamContent(texture)
        {
            Headers =
            {
                ContentLength = texture.Length,
                ContentType = new MediaTypeHeaderValue("image/png")
            }
        }, "file", "skin.png"); // Pass the name of the form field, the file name, and the content type

        var request = await _skinServiceClient.PostAsync($"/cloak/{authUser.Name}", content);

        if (!request.IsSuccessStatusCode)
            return null;

        var data = await request.Content.ReadAsStringAsync();

        return JsonConvert.DeserializeObject<TextureReadDto>(data)?.ClockUrl;
    }
}

public interface ISkinServiceManager
{
    Task<string?> UpdateSkin(AuthUser authUser, Stream texture);
    Task<string?> UpdateCloak(AuthUser authUser, Stream texture);
}