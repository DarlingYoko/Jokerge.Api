using System.Net;
using Gml.Dto.Messages;
using Gml.Web.Api.Core.Metrics;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;

namespace Gml.WebApi.Tests.Metrics;

/// <summary>The panel's «Метрики» page and the middleware that feeds it.</summary>
public class MetricsEndpointsTests
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

    private async Task<(HttpStatusCode Status, MetricsReportDto? Report)> GetReport(string period)
    {
        var response = await _httpClient.GetAsync($"/api/v1/metrics?period={period}");
        var body = JsonConvert.DeserializeObject<ResponseMessage<MetricsReportDto>>(
            await response.Content.ReadAsStringAsync());
        return (response.StatusCode, body?.Data);
    }

    [Test]
    public async Task Report_HasSeriesAndLiveProcessInfo()
    {
        var (status, report) = await GetReport("6h");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(report?.Series, Has.Count.EqualTo(72));
            Assert.That(report?.Live.UptimeSeconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(report?.Live.WorkingSetBytes, Is.GreaterThan(0));
            Assert.That(report?.Live.ProcessorCount, Is.GreaterThan(0));
            Assert.That(report?.Live.Databases, Is.Not.Empty);
        });
    }

    [Test]
    public async Task UnknownPeriod_IsBadRequest()
    {
        var response = await _httpClient.GetAsync("/api/v1/metrics?period=7d");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Requests_AreGroupedByRouteTemplate()
    {
        await _httpClient.GetAsync($"/api/v1/diagnostics/{Guid.NewGuid()}");
        await _httpClient.GetAsync($"/api/v1/diagnostics/{Guid.NewGuid()}");
        await _httpClient.GetAsync("/no-such-page-" + Guid.NewGuid());

        var (_, report) = await GetReport("1h");
        var routes = report!.Endpoints.ToDictionary(e => e.Route);

        Assert.Multiple(() =>
        {
            Assert.That(routes["GET /api/v1/diagnostics/{id}"].Requests, Is.GreaterThanOrEqualTo(2));
            Assert.That(routes["GET /api/v1/diagnostics/{id}"].ClientErrors, Is.GreaterThanOrEqualTo(2));
            Assert.That(routes.ContainsKey(RouteNormalizer.Unmatched), Is.True);
            Assert.That(routes.Keys.Any(r => r.Contains("no-such-page")), Is.False);
        });
    }
}
