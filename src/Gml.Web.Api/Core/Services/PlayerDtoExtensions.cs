using Gml.Dto.Player;

namespace Gml.Web.Api.Core.Services;

public static class PlayerDtoExtensions
{
    /// <summary>
    ///     Clears the launcher access token before a player list goes to the admin panel. The panel
    ///     never uses it, and anyone holding it can sign in to the game as that player.
    /// </summary>
    public static List<T> WithoutSecrets<T>(this List<T> players) where T : PlayerReadDto
    {
        foreach (var player in players)
            player.AccessToken = string.Empty;

        return players;
    }

    public static T WithoutSecrets<T>(this T player) where T : PlayerReadDto
    {
        player.AccessToken = string.Empty;
        return player;
    }
}
