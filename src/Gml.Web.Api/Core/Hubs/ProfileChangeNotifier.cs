using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace Gml.Web.Api.Core.Hubs;

public static class ProfileChangeNotifier
{
    /// <summary>Quiet period after the last profile save before launchers are told to reload.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     One notification per burst of profile saves. Adding ten players to a whitelist used to
    ///     make every online launcher reload its profile list ten times.
    /// </summary>
    public static IObservable<T> Coalesce<T>(IObservable<T> changes, TimeSpan window, IScheduler scheduler) =>
        changes.Throttle(window, scheduler);
}
