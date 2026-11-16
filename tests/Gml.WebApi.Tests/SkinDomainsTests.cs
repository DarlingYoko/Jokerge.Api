using System.Net.Http.Json;
using System.Text.Json;
using Gml.Web.Api.Core.Extensions;
using Gml.Web.Api.Core.Helpers;
using Gml.Web.Api.Core.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gml.WebApi.Tests;

// The same backend is reachable under several names (direct and relayed); players who logged in
// through different names must still accept each other's skins, and the skin URLs the game gets
// must point at a name every player can reach.
public class SkinDomainsTests
{
    [Test]
    public void TextureAddress_UsesRequestHost_WhenNoPublicHostConfigured()
    {
        Assert.That(TextureAddress.Build("https", null, "launcher.example.com"),
            Is.EqualTo("https://launcher.example.com"));
        Assert.That(TextureAddress.Build("https", " ", "launcher.example.com"),
            Is.EqualTo("https://launcher.example.com"));
    }

    [Test]
    public void TextureAddress_UsesPublicHost_WhenConfigured()
    {
        Assert.That(TextureAddress.Build("https", "launcher.ru.example.com", "launcher.example.com"),
            Is.EqualTo("https://launcher.ru.example.com"));
    }

    [Test]
    public void ParseList_SplitsOnCommasAndWhitespace_DroppingEmpties()
    {
        Assert.That(ApplicationExtensions.ParseList(" .example.com, launcher.ru.example.com ,, x.example.net"),
            Is.EqualTo(new[] { ".example.com", "launcher.ru.example.com", "x.example.net" }));
        Assert.That(ApplicationExtensions.ParseList(""), Is.Empty);
    }

    [Test]
    public async Task Metadata_ListsConfiguredSkinDomains_NextToTheRequestHost()
    {
        // Same environment the main Tests fixture starts the app with.
        Environment.SetEnvironmentVariable("SECURITY_KEY", "jkuhbsfgvuk4gfikhn8i7wa34rkbqw23");
        Environment.SetEnvironmentVariable("PROJECT_NAME", "GmlServer");
        Environment.SetEnvironmentVariable("MARKET_ENDPOINT", "https://gml-market.recloud.tech");
        Environment.SetEnvironmentVariable("PROJECT_DESCRIPTION", "GmlServer Description");
        Environment.SetEnvironmentVariable("PROJECT_POLICYNAME", "GmlPolicy");
        Environment.SetEnvironmentVariable("PROJECT_PATH", "");
        Environment.SetEnvironmentVariable("SERVICE_TEXTURE_ENDPOINT", "http://gml-web-skins:8085");

        await using var factory = new ConfiguredSettingsFactory(new ServerSettings
        {
            SecurityKey = "jkuhbsfgvuk4gfikhn8i7wa34rkbqw23",
            ProjectName = "GmlServer",
            SkinDomains = [".example.org"]
        });
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/integrations/authlib/minecraft");
        request.Headers.Add("X-Forwarded-Host", "launcher.ru.example.com");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var domains = body.GetProperty("skinDomains").EnumerateArray().Select(d => d.GetString()).ToList();
        Assert.That(domains, Does.Contain(".example.org"));
        Assert.That(domains, Does.Contain("launcher.ru.example.com"));
    }

    private sealed class ConfiguredSettingsFactory(ServerSettings settings) : GmlApiApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ServerSettings>();
                services.AddSingleton(settings);
            });
        }
    }
}
