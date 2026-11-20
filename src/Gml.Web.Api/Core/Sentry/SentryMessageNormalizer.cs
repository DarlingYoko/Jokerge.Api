using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GmlCore.Interfaces.Launcher;
using GmlCore.Interfaces.Sentry;

namespace Gml.Web.Api.Core.Sentry;

/// <summary>
///     Groups reports of the same problem. Exception messages carry per-machine details (profile
///     paths, install folders, ids, addresses); grouping by the raw text made a separate
///     "1 user" row for every player hitting the same bug.
/// </summary>
public static partial class SentryMessageNormalizer
{
    [GeneratedRegex(@"(['""])(?:[A-Za-z]:\\|\\\\)[^'""\r\n]*?\\([^\\'""\r\n]+)\1")]
    private static partial Regex QuotedWindowsPath();

    [GeneratedRegex(@"(['""])/[^'""\r\n]*?/([^/'""\r\n]+)\1")]
    private static partial Regex QuotedUnixPath();

    [GeneratedRegex(@"(?i)\b([A-Z]:\\Users\\)[^\\'""\r\n]+")]
    private static partial Regex WindowsProfile();

    [GeneratedRegex(@"(/(?:home|Users)/)[^/'""\s]+")]
    private static partial Regex UnixProfile();

    [GeneratedRegex(@"(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b")]
    private static partial Regex Guid();

    [GeneratedRegex(@"\b\d{1,3}(?:\.\d{1,3}){3}(?::\d{1,5})?\b")]
    private static partial Regex IpAddress();

    [GeneratedRegex(@"(?i)\b0x[0-9a-f]{6,}\b")]
    private static partial Regex HexAddress();

    [GeneratedRegex(@"\b\d{6,}\b")]
    private static partial Regex LongNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var text = QuotedWindowsPath().Replace(message, "$1…\\$2$1");
        text = QuotedUnixPath().Replace(text, "$1…/$2$1");
        text = WindowsProfile().Replace(text, "$1<user>");
        text = UnixProfile().Replace(text, "$1<user>");
        text = Guid().Replace(text, "<id>");
        text = IpAddress().Replace(text, "<ip>");
        text = HexAddress().Replace(text, "0x…");
        text = LongNumber().Replace(text, "<n>");

        return Whitespace().Replace(text, " ").Trim();
    }

    /// <summary>The root-cause exception Sentry lists first.</summary>
    public static IExceptionReport? RootException(IBugInfo bug) => bug.Exceptions?.FirstOrDefault();

    public static string Fingerprint(IBugInfo bug)
    {
        var root = RootException(bug);
        var key = $"{(int)bug.ProjectType}|{root?.Type}|{Normalize(root?.ValueData)}";

        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
    }
}
