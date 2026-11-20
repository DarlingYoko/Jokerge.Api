namespace Gml.Web.Api.Core.Sentry;

/// <summary>Inclusive time window in UTC.</summary>
public readonly record struct SentryTimeRange(DateTime FromUtc, DateTime ToUtc)
{
    public bool Contains(DateTime utc) => utc >= FromUtc && utc <= ToUtc;
}

/// <summary>
///     Report times arrive in three flavours: UTC from Sentry envelopes, server-local from
///     backend captures, and whatever the dashboard sends. Everything is compared in UTC and
///     bucketed in the viewer's offset so "today" means the admin's today, not the container's.
/// </summary>
public static class SentryTime
{
    private const int MaxOffsetMinutes = 14 * 60;

    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
    };

    /// <summary>Wall-clock time of the viewer, for bucketing only.</summary>
    public static DateTime ToViewer(DateTime utc, int tzOffsetMinutes) =>
        DateTime.SpecifyKind(utc.AddMinutes(tzOffsetMinutes), DateTimeKind.Unspecified);

    /// <summary>Viewer offset in minutes east of UTC; falls back to the server's own offset.</summary>
    public static int ResolveOffset(int? tzOffsetMinutes)
    {
        if (tzOffsetMinutes is null)
            return (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes;

        return Math.Clamp(tzOffsetMinutes.Value, -MaxOffsetMinutes, MaxOffsetMinutes);
    }

    /// <summary>
    ///     Request bounds to a UTC window. A missing start means all time, a missing end means now.
    ///     An end without a time of day and without a zone (old clients send "yyyy-MM-dd") covers
    ///     that whole day; the list endpoint used to drop it.
    /// </summary>
    public static SentryTimeRange ResolveRange(DateTime? from, DateTime? to, DateTime nowUtc)
    {
        var fromUtc = from is null ? DateTime.MinValue : ToUtc(from.Value);

        DateTime toUtc;
        if (to is null)
            toUtc = nowUtc;
        else if (to.Value.Kind == DateTimeKind.Unspecified && to.Value.TimeOfDay == TimeSpan.Zero)
            toUtc = ToUtc(to.Value.AddDays(1).AddTicks(-1));
        else
            toUtc = ToUtc(to.Value);

        if (toUtc < fromUtc)
            (fromUtc, toUtc) = (toUtc, fromUtc);

        return new SentryTimeRange(fromUtc, toUtc);
    }

    /// <summary>
    ///     Lower bound for the storage query. Storage rows carry the server-local receive time,
    ///     which is never earlier than the event, so a day of slack covers client clock skew.
    /// </summary>
    public static DateTime StorageLowerBound(SentryTimeRange range) =>
        range.FromUtc <= DateTime.MinValue.AddDays(2)
            ? DateTime.MinValue
            : range.FromUtc.AddDays(-1).ToLocalTime();
}
