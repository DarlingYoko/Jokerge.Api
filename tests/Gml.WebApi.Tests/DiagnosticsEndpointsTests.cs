using System.Net;
using Gml.Dto.Messages;
using Gml.Web.Api.Core.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;

namespace Gml.WebApi.Tests;

/// <summary>Routes behind the launcher's «Диагностика» button and the panel's diagnostics page.</summary>
public class DiagnosticsEndpointsTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _httpClient = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        Environment.SetEnvironmentVariable("SECURITY_KEY", "jkuhbsfgvuk4gfikhn8i7wa34rkbqw23");
        Environment.SetEnvironmentVariable("PROJECT_NAME", "GmlServer");
        Environment.SetEnvironmentVariable("PROJECT_DESCRIPTION", "GmlServer Description");
        Environment.SetEnvironmentVariable("PROJECT_POLICYNAME", "GmlPolicy");
        Environment.SetEnvironmentVariable("PROJECT_PATH", "");
        Environment.SetEnvironmentVariable("SERVICE_TEXTURE_ENDPOINT", "http://gml-web-skins:8085");
        Environment.SetEnvironmentVariable("MARKET_ENDPOINT", "https://gml-market.recloud.tech");

        _factory = new GmlApiApplicationFactory();
        _httpClient = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        _httpClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task Submit_WithoutValidPlayerToken_IsForbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/launcher/diagnostics")
        {
            Content = TestHelper.CreateJsonObject(new DiagnosticReportSubmitDto
            {
                UserName = "nobody-" + Guid.NewGuid().ToString("N"),
                Report = new DiagnosticReportDto { Text = "x" }
            })
        };
        request.Headers.TryAddWithoutValidation("Authorization", "not-a-token");

        var response = await _httpClient.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Submit_EmptyBody_IsBadRequest()
    {
        var response = await _httpClient.PostAsync("/api/v1/launcher/diagnostics",
            TestHelper.CreateJsonObject(new { userName = "yoko" }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task List_ReturnsPage()
    {
        var response = await _httpClient.GetAsync("/api/v1/diagnostics?take=5");
        var body = JsonConvert.DeserializeObject<ResponseMessage<DiagnosticReportPageDto>>(
            await response.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body?.Data?.Items, Is.Not.Null);
        });
    }

    [Test]
    public async Task Get_Unknown_IsNotFound()
    {
        var response = await _httpClient.GetAsync($"/api/v1/diagnostics/{Guid.NewGuid()}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
