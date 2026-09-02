using Gml.Core.Launcher;
using Gml.Domains.Launcher;
using Gml.Web.Api.Core.Middlewares;
using GmlCore.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gml.WebApi.Tests;

internal class GmlApiApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            //
            // services.AddAuthentication("TestScheme")
            //     .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("TestScheme", options => { });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "TestScheme";
                options.DefaultScheme = "TestScheme";
                options.DefaultChallengeScheme = "TestScheme";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("TestScheme", _ => { });

            // Re-register IGmlManager with a fake-handler-wrapped HttpClient so Forge/maven lookups
            // (see FakeForgeHttpHandler) never leave the machine during a test run.
            services.RemoveAll<IGmlManager>();
            services.AddSingleton<IGmlManager>(_ =>
            {
                var httpClient = new HttpClient(new FakeForgeHttpHandler(new HttpClientHandler()));

                var settings = new GmlSettings(
                    Environment.GetEnvironmentVariable("PROJECT_NAME") ?? "GmlServer",
                    Environment.GetEnvironmentVariable("SECURITY_KEY") ?? string.Empty,
                    Environment.GetEnvironmentVariable("PROJECT_PATH"),
                    httpClient)
                {
                    TextureServiceEndpoint =
                        Environment.GetEnvironmentVariable("SERVICE_TEXTURE_ENDPOINT") ?? "http://gml-web-skins:8085",
                };

                var manager = new GmlManager(settings);
                manager.RestoreSettings<LauncherVersion>();

                return manager;
            });
        });
    }
}
