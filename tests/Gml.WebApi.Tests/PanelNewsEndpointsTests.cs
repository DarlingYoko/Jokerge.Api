using System.Net;
using Gml.Dto.Messages;
using Gml.Dto.News;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;

namespace Gml.WebApi.Tests;

/// <summary>
///     The admin creates, edits and removes news in the panel; the launcher reads them through the
///     existing news feed endpoint, so no launcher rebuild is needed.
/// </summary>
public class PanelNewsEndpointsTests
{
    private readonly List<int> _created = [];
    private WebApplicationFactory<Program>? _factory;
    private HttpClient _httpClient = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        Environment.SetEnvironmentVariable("SECURITY_KEY", "jkuhbsfgvuk4gfikhn8i7wa34rkbqw23");
        Environment.SetEnvironmentVariable("PROJECT_NAME", "GmlServer");
        Environment.SetEnvironmentVariable("MARKET_ENDPOINT", "https://gml-market.recloud.tech");
        Environment.SetEnvironmentVariable("PROJECT_DESCRIPTION", "GmlServer Description");
        Environment.SetEnvironmentVariable("PROJECT_POLICYNAME", "GmlPolicy");
        Environment.SetEnvironmentVariable("PROJECT_PATH", "");
        Environment.SetEnvironmentVariable("SERVICE_TEXTURE_ENDPOINT", "http://gml-web-skins:8085");

        _factory = new GmlApiApplicationFactory();
        _httpClient = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        foreach (var id in _created) await _httpClient.DeleteAsync($"/api/v1/news/{id}");

        _httpClient.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
    }

    private static async Task<T?> Read<T>(HttpResponseMessage response)
    {
        return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync());
    }

    private async Task<PanelNewsReadDto> Create(string title, string content = "**hi**")
    {
        var response = await _httpClient.PostAsync("/api/v1/news",
            TestHelper.CreateJsonObject(new PanelNewsWriteDto
            {
                Title = title, Content = content, ProfileName = "Survival", BuildVersion = "1.0"
            }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
            await response.Content.ReadAsStringAsync());

        var created = (await Read<ResponseMessage<PanelNewsReadDto>>(response))!.Data!;
        _created.Add(created.Id);
        return created;
    }

    [Test]
    public async Task Create_ThenListedForPanelAndLauncher()
    {
        var title = "Вайп " + Guid.NewGuid().ToString("N");
        var created = await Create(title);

        var panel = await Read<ResponseMessage<List<PanelNewsReadDto>>>(
            await _httpClient.GetAsync("/api/v1/news"));
        var launcher = await Read<ResponseMessage<List<NewsReadDto>>>(
            await _httpClient.GetAsync("/api/v1/integrations/news/list"));

        Assert.Multiple(() =>
        {
            Assert.That(panel!.Data!.Single(n => n.Id == created.Id).Content, Is.EqualTo("**hi**"));
            Assert.That(launcher!.Data!.Single(n => n.Title == title).Content,
                Does.Contain("<strong>hi</strong>"));
        });
    }

    [Test]
    public async Task Update_ChangesNews()
    {
        var created = await Create("Черновик");

        var response = await _httpClient.PutAsync($"/api/v1/news/{created.Id}",
            TestHelper.CreateJsonObject(new PanelNewsWriteDto { Title = "Готово", Content = "done" }));
        var updated = (await Read<ResponseMessage<PanelNewsReadDto>>(response))!.Data!;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(updated.Title, Is.EqualTo("Готово"));
            Assert.That(updated.ProfileName, Is.Null);
        });
    }

    [Test]
    public async Task Delete_RemovesNews()
    {
        var created = await Create("Удалить");

        var response = await _httpClient.DeleteAsync($"/api/v1/news/{created.Id}");
        var panel = await Read<ResponseMessage<List<PanelNewsReadDto>>>(
            await _httpClient.GetAsync("/api/v1/news"));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(panel!.Data!.Any(n => n.Id == created.Id), Is.False);
        });
    }

    [Test]
    public async Task Create_EmptyTitle_IsRejected()
    {
        var response = await _httpClient.PostAsync("/api/v1/news",
            TestHelper.CreateJsonObject(new PanelNewsWriteDto { Title = " ", Content = "x" }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UpdateAndDelete_UnknownId_ReturnNotFound()
    {
        var update = await _httpClient.PutAsync("/api/v1/news/987654321",
            TestHelper.CreateJsonObject(new PanelNewsWriteDto { Title = "x", Content = "x" }));
        var delete = await _httpClient.DeleteAsync("/api/v1/news/987654321");

        Assert.Multiple(() =>
        {
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }
}
