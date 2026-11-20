using Gml.Web.Api.Core.Diagnostics;

namespace Gml.WebApi.Tests;

/// <summary>
///     Diagnostics reports end up in the admin panel; tokens, passwords and the player's Windows
///     account name must not. The launcher masks before sending, the server masks again.
/// </summary>
public class DiagnosticTextRedactorTests
{
    [Test]
    public void WindowsProfilePath_HidesAccountName_KeepsRest()
    {
        var text = DiagnosticTextRedactor.Redact(@"Game folder: C:\Users\Artem Zaitsev\AppData\Roaming\jokerge");

        Assert.That(text, Is.EqualTo(@"Game folder: C:\Users\<user>\AppData\Roaming\jokerge"));
    }

    [Test]
    public void WindowsProfilePath_WithForwardSlashes_IsMasked()
    {
        Assert.That(DiagnosticTextRedactor.Redact("C:/Users/yoko/AppData"), Is.EqualTo("C:/Users/<user>/AppData"));
    }

    [Test]
    public void UnixHome_IsMasked()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DiagnosticTextRedactor.Redact("/home/yoko/.jokerge/logs"), Is.EqualTo("/home/<user>/.jokerge/logs"));
            Assert.That(DiagnosticTextRedactor.Redact("/Users/yoko/Library"), Is.EqualTo("/Users/<user>/Library"));
        });
    }

    [Test]
    public void Jwt_IsMasked()
    {
        var jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ5b2tvIn0.dGhpc2lzYXNpZ25hdHVyZQ";

        Assert.That(DiagnosticTextRedactor.Redact("token " + jwt + " end"), Is.EqualTo("token <token> end"));
    }

    [Test]
    public void BearerHeader_IsMasked()
    {
        Assert.That(DiagnosticTextRedactor.Redact("Authorization: Bearer abc.DEF-123_x"),
            Is.EqualTo("Authorization: Bearer <token>"));
    }

    [TestCase("password=hunter2", "password=<hidden>")]
    [TestCase("accessToken: abc123", "accessToken: <hidden>")]
    [TestCase("\"access_token\":\"abc123\"", "\"access_token\":\"<hidden>\"")]
    [TestCase("api-key=qwe&x=1", "api-key=<hidden>&x=1")]
    [TestCase("--accessToken 0123456789abcdef --version 1.20", "--accessToken <hidden> --version 1.20")]
    public void SecretKeyValuePairs_AreMasked(string input, string expected)
    {
        Assert.That(DiagnosticTextRedactor.Redact(input), Is.EqualTo(expected));
    }

    [Test]
    public void KnownSecrets_AreMaskedEverywhere()
    {
        var text = DiagnosticTextRedactor.Redact("token was s3cr3t-value, again s3cr3t-value", ["s3cr3t-value"]);

        Assert.That(text, Is.EqualTo("token was <hidden>, again <hidden>"));
    }

    [Test]
    public void ShortOrEmptyKnownSecrets_AreIgnored()
    {
        Assert.That(DiagnosticTextRedactor.Redact("a b c", ["", null, "a"]), Is.EqualTo("a b c"));
    }

    [Test]
    public void OrdinaryText_IsUntouched()
    {
        const string text = "API https://launcher.jokerge.com answered 200 in 42 ms; Java 21.0.3 at runtime\\java-21\\bin";

        Assert.That(DiagnosticTextRedactor.Redact(text), Is.EqualTo(text));
    }
}
