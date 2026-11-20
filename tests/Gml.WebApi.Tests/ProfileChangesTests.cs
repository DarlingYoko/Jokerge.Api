using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using Gml.Dto.Player;
using Gml.Web.Api.Core.Hubs;
using Gml.Web.Api.Core.Services;

namespace Gml.WebApi.Tests;

/// <summary>
///     Adding players to a profile one by one saved the profiles after each click, and every save
///     told every online launcher to reload its profile list. The panel also received each
///     whitelisted player's launcher access token, which it never uses.
/// </summary>
public class ProfileChangesTests
{
    [Test]
    public void Coalesce_BurstOfChanges_NotifiesOnceAfterItSettles()
    {
        var scheduler = new HistoricalScheduler();
        var changes = new Subject<int>();
        var notified = new List<DateTimeOffset>();

        using var _ = ProfileChangeNotifier.Coalesce(changes, TimeSpan.FromSeconds(2), scheduler)
            .Subscribe(_ => notified.Add(scheduler.Now));

        for (var i = 0; i < 10; i++)
        {
            changes.OnNext(0);
            scheduler.AdvanceBy(TimeSpan.FromMilliseconds(500));
        }

        scheduler.AdvanceBy(TimeSpan.FromSeconds(5));
        changes.OnNext(0);
        scheduler.AdvanceBy(TimeSpan.FromSeconds(5));

        Assert.That(notified, Has.Count.EqualTo(2));
    }

    [Test]
    public void WithoutSecrets_ClearsAccessTokens()
    {
        var players = new List<PlayerReadDto>
        {
            new() { Name = "a", Uuid = "1", AccessToken = "secret-a" },
            new ExtendedPlayerReadDto { Name = "b", Uuid = "2", AccessToken = "secret-b" }
        };

        var result = players.WithoutSecrets();

        Assert.Multiple(() =>
        {
            Assert.That(result.Select(p => p.AccessToken), Is.All.Empty);
            Assert.That(result.Select(p => p.Name), Is.EqualTo(new[] { "a", "b" }));
        });
    }
}
