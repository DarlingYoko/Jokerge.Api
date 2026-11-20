using Gml.Web.Api.Core.Metrics;

namespace Gml.WebApi.Tests.Metrics;

public class RouteNormalizerTests
{
    [TestCase("/api/v1/diagnostics/{id:guid}", "GET /api/v1/diagnostics/{id}")]
    [TestCase("api/v1/news/{id:int}", "GET /api/v1/news/{id}")]
    [TestCase("/api/v1/file/{fileHash}", "GET /api/v1/file/{fileHash}")]
    [TestCase("/api/v1/items/{id:int?}", "GET /api/v1/items/{id?}")]
    [TestCase("/api/v1/files/{*path:nonfile}", "GET /api/v1/files/{*path}")]
    [TestCase("/api/v1/page/{take=20}", "GET /api/v1/page/{take}")]
    public void RoutePattern_IsUsedWithoutConstraints(string pattern, string expected)
    {
        Assert.That(RouteNormalizer.Normalize("get", pattern, "/ignored/123", 200), Is.EqualTo(expected));
    }

    [Test]
    public void NoRoute_NotFound_IsOneBucket()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RouteNormalizer.Normalize("GET", null, "/wp-login.php", 404),
                Is.EqualTo(RouteNormalizer.Unmatched));
            Assert.That(RouteNormalizer.Normalize("POST", null, "/.env", 404),
                Is.EqualTo(RouteNormalizer.Unmatched));
        });
    }

    [Test]
    public void NoRoute_PluginPath_ReplacesIdsInTheRawPath()
    {
        var route = RouteNormalizer.Normalize("GET", null,
            "/api/v1/plugins/Shop/Items/123/3f2504e0-4f89-11d3-9a0c-0305e82c3301/", 200);

        Assert.That(route, Is.EqualTo("GET /api/v1/plugins/shop/items/{id}/{guid}"));
    }

    [Test]
    public void NoRoute_HashSegment_IsCollapsed()
    {
        var route = RouteNormalizer.Normalize("GET", null, "/files/0a1b2c3d4e5f60718293a4b5c6d7e8f901234567", 200);

        Assert.That(route, Is.EqualTo("GET /files/{hash}"));
    }

    [Test]
    public void Root_IsSlash()
    {
        Assert.That(RouteNormalizer.Normalize("GET", null, "", 302), Is.EqualTo("GET /"));
    }

    [TestCase("/ws/launcher", false)]
    [TestCase("/ws/launcher/negotiate", false)]
    [TestCase("/WS/audit", false)]
    [TestCase("/health", false)]
    [TestCase("/api/v1/metrics", true)]
    [TestCase("/api/v1/wsx", true)]
    [TestCase("/", true)]
    public void IsTracked_SkipsHubsAndHealthChecks(string path, bool expected)
    {
        Assert.That(RouteNormalizer.IsTracked(path), Is.EqualTo(expected));
    }
}
