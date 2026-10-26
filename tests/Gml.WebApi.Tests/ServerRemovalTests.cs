using Gml.Models.Servers;
using Gml.Web.Api.Core.Handlers;
using GmlCore.Interfaces.Servers;

namespace Gml.WebApi.Tests;

public class ServerRemovalTests
{
    private static List<IProfileServer> Servers(params string[] names) =>
        names.Select(name => (IProfileServer)new MinecraftServer { Name = name }).ToList();

    [Test]
    public void FindServersToRemove_MatchesSingleName()
    {
        var servers = Servers("Survival", "Creative");

        var found = ServersHandler.FindServersToRemove(servers, "Creative");

        Assert.That(found.Select(c => c.Name), Is.EqualTo(new[] { "Creative" }));
    }

    [Test]
    public void FindServersToRemove_MatchesCommaSeparatedNames()
    {
        var servers = Servers("Survival", "Creative", "Lobby");

        var found = ServersHandler.FindServersToRemove(servers, "Survival,Lobby");

        Assert.That(found.Select(c => c.Name), Is.EquivalentTo(new[] { "Survival", "Lobby" }));
    }

    [Test]
    public void FindServersToRemove_NameContainingCommaIsNotSplit()
    {
        var servers = Servers("Survival, hardcore", "Survival");

        var found = ServersHandler.FindServersToRemove(servers, "Survival, hardcore");

        Assert.That(found.Select(c => c.Name), Is.EqualTo(new[] { "Survival, hardcore" }));
    }

    [Test]
    public void FindServersToRemove_RemovesAllDuplicatesWithSameName()
    {
        var servers = Servers("Survival", "Survival", "Creative");

        var found = ServersHandler.FindServersToRemove(servers, "Survival");

        Assert.That(found, Has.Count.EqualTo(2));
    }

    [Test]
    public void FindServersToRemove_MatchesNameWithStrayWhitespace()
    {
        // Browsers strip trailing spaces from the URL, so "Survival " arrives as "Survival".
        var servers = Servers("Survival ", "Creative");

        var found = ServersHandler.FindServersToRemove(servers, "Survival");

        Assert.That(found.Select(c => c.Name), Is.EqualTo(new[] { "Survival " }));
    }

    [Test]
    public void FindServersToRemove_ReturnsEmptyWhenNothingMatches()
    {
        var servers = Servers("Survival");

        Assert.That(ServersHandler.FindServersToRemove(servers, "Missing"), Is.Empty);
    }
}
