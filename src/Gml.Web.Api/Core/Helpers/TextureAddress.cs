namespace Gml.Web.Api.Core.Helpers;

/// <summary>
/// Base address that skin and cape URLs handed to the game are built on.
/// </summary>
public static class TextureAddress
{
    /// <summary>
    /// <paramref name="publicHost"/> when configured, otherwise the host the request came in on.
    /// The game proxy asks for player profiles under one name, but those URLs end up on every
    /// player's client, so they must use a name all players can reach (see TEXTURE_PUBLIC_HOST).
    /// </summary>
    public static string Build(string protocol, string? publicHost, string requestHost)
    {
        var host = string.IsNullOrWhiteSpace(publicHost) ? requestHost : publicHost.Trim();
        return $"{protocol}://{host}";
    }
}
