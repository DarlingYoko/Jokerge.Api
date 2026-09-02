using System.Net;
using System.Text;

namespace Gml.WebApi.Tests;

/// <summary>
/// Intercepts outbound HTTP calls to Forge's maven/download hosts so the test suite never reaches the
/// real network for Forge version lookups. Forge's official download page (files.minecraftforge.net)
/// wraps its installer links through an ad-gated redirect (historically adfoc.us) alongside a direct
/// maven.minecraftforge.net link — CmlLib.Core.Installer.Forge's ForgeVersionLoader scrapes that page's
/// HTML directly (GET https://files.minecraftforge.net/net/minecraftforge/forge/index_{mcVersion}.html),
/// which is what made every run of CreateProfile (GameLoader.Forge) hit that real page.
///
/// Everything else is passed through unchanged.
/// </summary>
internal sealed class FakeForgeHttpHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private const string ForgeHost = "files.minecraftforge.net";
    private const string ForgeMavenHost = "maven.minecraftforge.net";

    // (minecraftVersion, forgeVersion) pairs the fake version-list page should report as available.
    // Extend this if a test starts exercising another Forge version.
    private static readonly (string McVersion, string ForgeVersion)[] KnownForgeVersions =
    [
        ("1.7.10", "10.13.4.1614")
    ];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host;

        if (host == ForgeHost && (request.RequestUri!.AbsolutePath.Contains("/index_")))
        {
            var mcVersion = ExtractMcVersionFromIndexPath(request.RequestUri.AbsolutePath);
            return Task.FromResult(BuildVersionListResponse(mcVersion));
        }

        if (host is ForgeHost or ForgeMavenHost)
        {
            // Any other Forge request (e.g. an actual installer jar download) is deliberately not
            // faked — reproducing a real, working Forge install offline is out of scope, so the one
            // test that needs it (RestoreProfile) is marked [Explicit] instead of exercised by default.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static string? ExtractMcVersionFromIndexPath(string path)
    {
        // .../index_{mcVersion}.html
        var fileName = path[(path.LastIndexOf('/') + 1)..];
        const string prefix = "index_";
        const string suffix = ".html";
        if (!fileName.StartsWith(prefix) || !fileName.EndsWith(suffix)) return null;
        return fileName[prefix.Length..^suffix.Length];
    }

    private static HttpResponseMessage BuildVersionListResponse(string? mcVersion)
    {
        var rows = KnownForgeVersions
            .Where(v => mcVersion is null || v.McVersion == mcVersion)
            .Select(v => $"""
                <tr>
                  <td class="download-version">{v.ForgeVersion}</td>
                  <td class="download-time">2014-01-01 00:00:00 +0000</td>
                  <td class="download-files">
                    <ul>
                      <li><a href="#">Installer</a></li>
                    </ul>
                  </td>
                </tr>
                """);

        // Structure verified against CmlLib.Core.Installer.Forge's ForgeVersionLoader XPath:
        // //html[1]//body[1]//main[1]//div[2]//div[2]//div[2]//table[1]//tbody[1]//tr
        var html = $"""
            <html>
            <body>
            <main>
            <div>skip</div>
            <div>
            <div>skip</div>
            <div>
            <div>skip</div>
            <div>
            <table><tbody>
            {string.Join(Environment.NewLine, rows)}
            </tbody></table>
            </div>
            </div>
            </div>
            </main>
            </body>
            </html>
            """;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };
    }
}
