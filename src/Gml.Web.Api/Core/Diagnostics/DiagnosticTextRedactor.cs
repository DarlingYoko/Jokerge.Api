using System.Text.RegularExpressions;

namespace Gml.Web.Api.Core.Diagnostics;

/// <summary>
///     Masks secrets and the OS account name in diagnostics text, the same way
///     <see cref="Sentry.SentryMessageNormalizer" /> masks profile paths. The launcher applies the
///     same rules before sending; this is the server-side second pass.
/// </summary>
public static partial class DiagnosticTextRedactor
{
    private const string Hidden = "<hidden>";

    [GeneratedRegex(@"(?i)\b([A-Z]:[\\/]Users[\\/])[^\\/'""\r\n]+")]
    private static partial Regex WindowsProfile();

    [GeneratedRegex(@"(/(?:home|Users)/)[^/'""\s]+")]
    private static partial Regex UnixProfile();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?i)\b(Bearer)\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex Bearer();

    [GeneratedRegex(@"(?i)(--accessToken\s+)\S+")]
    private static partial Regex AccessTokenArgument();

    [GeneratedRegex(
        @"(?i)\b(access[_-]?token|token|password|passwd|pwd|secret|api[_-]?key|session[_-]?id)(""?\s*[:=]\s*""?)[^\s""'&,;]+")]
    private static partial Regex SecretPair();

    public static string Redact(string? text, IEnumerable<string?>? knownSecrets = null)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (knownSecrets is not null)
        {
            foreach (var secret in knownSecrets)
            {
                if (string.IsNullOrEmpty(secret) || secret.Length < 4) continue;
                text = text.Replace(secret, Hidden, StringComparison.OrdinalIgnoreCase);
            }
        }

        text = WindowsProfile().Replace(text, "$1<user>");
        text = UnixProfile().Replace(text, "$1<user>");
        text = Jwt().Replace(text, "<token>");
        text = Bearer().Replace(text, "$1 <token>");
        text = AccessTokenArgument().Replace(text, "$1" + Hidden);
        text = SecretPair().Replace(text, "$1$2" + Hidden);

        return text;
    }
}
