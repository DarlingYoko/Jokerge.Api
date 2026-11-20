using System.Text.RegularExpressions;

namespace Gml.Web.Api.Core.Metrics;

/// <summary>
///     Turns a request into a stable key like <c>GET /api/v1/profiles/{profileName}</c>: the route template when
///     ASP.NET matched one, otherwise the raw path with ids, GUIDs and hashes collapsed.
/// </summary>
public static partial class RouteNormalizer
{
    /// <summary>All 404s without a route (scanners, typos) share one row.</summary>
    public const string Unmatched = "(нет маршрута)";

    public static string Normalize(string method, string? routePattern, string path, int statusCode)
    {
        var verb = method.ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(routePattern))
            return $"{verb} /{ParameterRegex().Replace(routePattern.Trim().TrimStart('/'), StripConstraints)}";

        if (statusCode == StatusCodes.Status404NotFound)
            return Unmatched;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(NormalizeSegment);
        return $"{verb} /{string.Join('/', segments)}";
    }

    /// <summary>SignalR hubs hold a request open for the whole session; health checks are the orchestrator's.</summary>
    public static bool IsTracked(string path)
    {
        return !(path.StartsWith("/ws/", StringComparison.OrdinalIgnoreCase)
                 || path.Equals("/ws", StringComparison.OrdinalIgnoreCase)
                 || path.Equals("/health", StringComparison.OrdinalIgnoreCase));
    }

    private static string StripConstraints(Match match)
    {
        var catchAll = match.Groups["star"].Value;
        var name = match.Groups["name"].Value;
        var optional = match.Groups["rest"].Value.EndsWith('?') ? "?" : string.Empty;
        return $"{{{catchAll}{name}{optional}}}";
    }

    private static string NormalizeSegment(string segment)
    {
        if (Guid.TryParse(segment, out _))
            return "{guid}";
        if (segment.All(char.IsAsciiDigit))
            return "{id}";
        if (segment.Length >= 16 && segment.All(char.IsAsciiHexDigit))
            return "{hash}";
        return segment.ToLowerInvariant();
    }

    [GeneratedRegex(@"\{(?<star>\*{0,2})(?<name>[^}:=?*]+)(?<rest>[^}]*)\}")]
    private static partial Regex ParameterRegex();
}
