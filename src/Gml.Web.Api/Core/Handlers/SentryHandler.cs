using System.Net;
using Gml.Domains.Sentry;
using Gml.Dto.Messages;
using Gml.Dto.Sentry;
using Gml.Web.Api.Core.Sentry;
using Gml.Web.Api.Core.Services;
using GmlCore.Interfaces;
using GmlCore.Interfaces.Launcher;
using GmlCore.Interfaces.Sentry;

namespace Gml.Web.Api.Core.Handlers;

public abstract class SentryHandler : ISentryHandler
{
    public static async Task<IResult> CreateBugInfo(HttpContext context, IGmlManager gmlManager, int projectId)
    {
        using var memoryStream = new MemoryStream();
        await context.Request.Body.CopyToAsync(memoryStream);

        string envelope;
        try
        {
            envelope = await CompressionService.Uncompress(memoryStream.ToArray());
        }
        catch (System.Exception)
        {
            return Results.BadRequest(ResponseMessage.Create("Не удалось распаковать отчёт", HttpStatusCode.BadRequest));
        }

        var bug = SentryEnvelopeReader.Read(envelope, context.Connection.RemoteIpAddress?.ToString());

        // Transactions, sessions and events without an exception are accepted and dropped, so
        // the SDK does not retry them.
        if (bug is not null)
            gmlManager.BugTracker.CaptureException(bug);

        return Results.Empty;
    }

    public static async Task<IResult> SolveAllBugs(IGmlManager gmlManager)
    {
        await gmlManager.BugTracker.SolveAllAsync();

        return Results.Ok(ResponseMessage.Create("Все ошибки очищены", HttpStatusCode.OK));
    }

    public static async Task<IResult> ResolveIssue(IGmlManager gmlManager, SentryResolveIssueDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Fingerprint))
            return Results.BadRequest(ResponseMessage.Create("Не указана проблема", HttpStatusCode.BadRequest));

        var bugs = await gmlManager.BugTracker.GetFilteredBugs(_ => true);
        var ids = bugs
            .Where(b => SentryMessageNormalizer.RootException(b) is not null)
            .Where(b => SentryMessageNormalizer.Fingerprint(b) == request.Fingerprint)
            .Select(b => Guid.TryParse(b.Id, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        if (ids.Count == 0)
            return Results.NotFound(ResponseMessage.Create("Проблема не найдена", HttpStatusCode.NotFound));

        var removed = await gmlManager.BugTracker.RemoveBugsAsync(ids);

        return Results.Ok(ResponseMessage.Create(removed, $"Удалено событий: {removed}", HttpStatusCode.OK));
    }

    public static async Task<IResult> GetBugs(IGmlManager gmlManager, SentryFilterDto filter)
    {
        var range = SentryTime.ResolveRange(filter.DateFrom, filter.DateTo, DateTime.UtcNow);
        var bugs = (await LoadBugs(gmlManager, range))
            .Where(c => c.Exceptions.Any())
            .ToList();

        var error = new BaseSentryError
        {
            Bugs = bugs
                .GroupBy(bug => bug.Exceptions.First().Type)
                .Select(group => new SentryBugs
                {
                    Exception = group.Key,
                    Count = group.Count(),
                    CountUsers = group.Select(bug => bug.PcName).Distinct().Count(),
                    Graphics = group
                        .GroupBy(bug => SentryTime.ToUtc(bug.SendAt).Date)
                        .Select(dayGroup => new SentryGraphic { Month = dayGroup.Key, Count = dayGroup.Count() })
                        .ToList()
                })
                .ToList(),
            CountUsers = bugs.Select(x => x.PcName).Distinct().Count(),
            Count = bugs.Count
        };

        return Results.Ok(ResponseMessage.Create(error, "Все ошибки", HttpStatusCode.OK));
    }

    public static async Task<IResult> GetFilterSentry(IGmlManager gmlManager, SentryFilterDto filter)
    {
        var range = SentryTime.ResolveRange(filter.DateFrom, filter.DateTo, DateTime.UtcNow);
        var projectType = filter.ProjectType == 0 ? ProjectType.All : filter.ProjectType;

        var byProject = (await LoadBugs(gmlManager, range)).Where(c => (projectType & c.ProjectType) != 0);

        return Results.Ok(ResponseMessage.Create(byProject, "Отфильтрованные ошибки", HttpStatusCode.OK));
    }

    /// <summary>Problems list for the dashboard: one row per distinct problem in the window.</summary>
    public static async Task<IResult> GetFilterListSentry(IGmlManager gmlManager, SentryIssuesFilterDto filter)
    {
        var range = SentryTime.ResolveRange(filter.DateFrom, filter.DateTo, DateTime.UtcNow);
        var projectType = filter.ProjectType == 0 ? ProjectType.All : filter.ProjectType;

        var bugs = (await LoadBugs(gmlManager, range)).Where(c => (projectType & c.ProjectType) != 0);
        var issues = SentryIssueBuilder.Build(bugs, range, SentryTime.ResolveOffset(filter.TzOffset));

        return Results.Ok(ResponseMessage.Create(issues, "Отфильтрованные ошибки", HttpStatusCode.OK));
    }

    public static async Task<IResult> GetLastSentryErrors(IGmlManager gmlManager, int? tzOffset)
    {
        const int days = 90;
        var now = DateTime.UtcNow;
        var range = new SentryTimeRange(now.AddDays(-days - 1), now);

        var bugs = await LoadBugs(gmlManager, range);
        var stats = SentryStatsBuilder.BuildDaily(bugs, now, SentryTime.ResolveOffset(tzOffset), days);

        return Results.Ok(ResponseMessage.Create(stats, "Ошибки за последние 3 месяца", HttpStatusCode.OK));
    }

    public static async Task<IResult> GetSummarySentryErrors(IGmlManager gmlManager, int? tzOffset)
    {
        var now = DateTime.UtcNow;
        var range = new SentryTimeRange(now.AddDays(-SentryStatsBuilder.SummaryDays), now);

        var bugs = await LoadBugs(gmlManager, range);
        var total = await gmlManager.BugTracker.CountBugsAsync();
        var summary = SentryStatsBuilder.BuildSummary(bugs, total, now, SentryTime.ResolveOffset(tzOffset));

        return Results.Ok(ResponseMessage.Create(summary, "Статистика", HttpStatusCode.OK));
    }

    public static async Task<IResult> GetByException(IGmlManager gmlManager, string exception)
    {
        var group = (await gmlManager.BugTracker.GetAllBugs())
            .Where(bug => SentryMessageNormalizer.RootException(bug)?.Type == exception)
            .ToList();

        var result = new SentryExceptionReadDto
        {
            Exception = exception,
            Count = group.Count,
            CountUsers = group.Select(bug => bug.PcName).Distinct().Count(),
            StackTrace = group.Count == 0 ? string.Empty : SentryStackTraceFormatter.Format(group[^1].Exceptions),
            OperationSystems = group
                .GroupBy(x => x.OsVersion)
                .Select(os => new SentryOperationSystem { Count = os.Count(), OsType = os.Key ?? string.Empty })
                .ToList(),
            Graphic = group
                .GroupBy(bug => SentryTime.ToUtc(bug.SendAt).Date)
                .Select(day => new SentryGraphic { Month = day.Key, Count = day.Count() })
                .ToList(),
            BugInfo = group.LastOrDefault()!
        };

        return Results.Ok(ResponseMessage.Create(result, "Информация об ошибке", HttpStatusCode.OK));
    }

    public static async Task<IResult> GetBugId(IGmlManager gmlManager, string id)
    {
        var bug = Guid.TryParse(id, out var guid) ? await gmlManager.BugTracker.GetBugId(guid) : null;

        if (bug is null)
            return Results.NotFound(ResponseMessage.Create("Ошибка не найдена", HttpStatusCode.NotFound));

        return Results.Ok(ResponseMessage.Create(bug, "Bug info", HttpStatusCode.OK));
    }

    private static Task<IEnumerable<IBugInfo>> LoadBugs(IGmlManager gmlManager, SentryTimeRange range)
    {
        var lowerBound = SentryTime.StorageLowerBound(range);

        return lowerBound == DateTime.MinValue
            ? gmlManager.BugTracker.GetFilteredBugs(_ => true)
            : gmlManager.BugTracker.GetFilteredBugs(c => c.Date >= lowerBound);
    }
}
