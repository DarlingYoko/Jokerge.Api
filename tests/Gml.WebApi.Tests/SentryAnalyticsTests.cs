using Gml.Core.Launcher;
using Gml.Web.Api.Core.Sentry;
using GmlCore.Interfaces.Launcher;
using GmlCore.Interfaces.Sentry;

namespace Gml.WebApi.Tests;

/// <summary>
///     The "Errors" dashboard tab used to split one problem into a row per player (the message
///     carried each player's profile path), never sorted rows, dropped the last day of a picked
///     range and compared browser-local times with server-local ones. These pin the fixed behaviour.
/// </summary>
public class SentryAnalyticsTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private static BugInfo Bug(string type, string message, DateTime sendAtUtc, string user = "yoko",
        string pc = "DESKTOP-1", ProjectType project = ProjectType.Launcher, string? release = null,
        IEnumerable<IExceptionReport>? exceptions = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        SendAt = sendAtUtc,
        Username = user,
        PcName = pc,
        IpAddress = "10.0.0.1",
        OsVersion = "Microsoft Windows 10.0.22631",
        Release = release,
        ProjectType = project,
        MemoryInfo = new MemoryInfo(),
        Exceptions = exceptions?.ToList() ?? new List<IExceptionReport>
        {
            new ExceptionReport { Type = type, ValueData = message, Module = "m", StackTrace = [] }
        }
    };

    private static string ProfilesLocked(string user) =>
        $"The process cannot access the file 'C:\\Users\\{user}\\AppData\\Roaming\\jokerge\\offline-mode\\profiles.json' because it is being used by another process.";

    #region Message normalization

    [Test]
    public void Normalize_SameFileUnderDifferentProfiles_IsOneMessage()
    {
        Assert.That(SentryMessageNormalizer.Normalize(ProfilesLocked("yoko")),
            Is.EqualTo(SentryMessageNormalizer.Normalize(ProfilesLocked("vipig"))));
    }

    [Test]
    public void Normalize_QuotedAbsolutePath_KeepsFileName()
    {
        var a = SentryMessageNormalizer.Normalize(
            "The process cannot access the file 'F:\\Jokerge\\game data\\Waffle\\config\\packetfixer.properties' because it is being used by another process.");

        Assert.That(a, Does.Contain("packetfixer.properties").And.Not.Contain("Waffle"));
    }

    [Test]
    public void Normalize_DifferentFiles_StayDifferent()
    {
        var profiles = SentryMessageNormalizer.Normalize(ProfilesLocked("yoko"));
        var other = SentryMessageNormalizer.Normalize(ProfilesLocked("yoko").Replace("profiles.json", "settings.json"));

        Assert.That(profiles, Is.Not.EqualTo(other));
    }

    [Test]
    public void Normalize_GuidsAndIps_AreMasked_StatusCodesKept()
    {
        var a = SentryMessageNormalizer.Normalize("Request 3f2504e0-4f89-11d3-9a0c-0305e82c3301 to 192.168.1.97:5000 failed: 500 (Internal Server Error).");
        var b = SentryMessageNormalizer.Normalize("Request 6ba7b810-9dad-11d1-80b4-00c04fd430c8 to 10.0.0.2:443 failed: 500 (Internal Server Error).");

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Does.Contain("500"));
        });
    }

    [Test]
    public void Fingerprint_DependsOnProject()
    {
        var launcher = Bug("System.Exception", "boom", Now);
        var backend = Bug("System.Exception", "boom", Now, project: ProjectType.Backend);

        Assert.That(SentryMessageNormalizer.Fingerprint(launcher),
            Is.Not.EqualTo(SentryMessageNormalizer.Fingerprint(backend)));
    }

    #endregion

    #region Time ranges

    [Test]
    public void ResolveRange_UtcInstants_AreKept()
    {
        var from = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

        var range = SentryTime.ResolveRange(from, to, Now);

        Assert.That((range.FromUtc, range.ToUtc), Is.EqualTo((from, to)));
    }

    [Test]
    public void ResolveRange_DateOnlyEnd_CoversWholeDay()
    {
        // Old clients sent "yyyy-MM-dd" and the list endpoint then cut the last day off.
        var day = new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Unspecified);

        var range = SentryTime.ResolveRange(day, day, Now);

        Assert.That(range.ToUtc - range.FromUtc, Is.EqualTo(TimeSpan.FromDays(1) - TimeSpan.FromTicks(1)));
    }

    [Test]
    public void ResolveRange_Missing_IsAllTimeUntilNow()
    {
        var range = SentryTime.ResolveRange(null, null, Now);

        Assert.That((range.FromUtc, range.ToUtc), Is.EqualTo((DateTime.MinValue, Now)));
    }

    #endregion

    #region Issues

    [Test]
    public void BuildIssues_GroupsPlayersOfOneProblem()
    {
        var bugs = new[]
        {
            Bug("System.IO.IOException", ProfilesLocked("yoko"), Now.AddHours(-3), "yoko", "PC-A"),
            Bug("System.IO.IOException", ProfilesLocked("yoko"), Now.AddHours(-2), "yoko", "PC-A"),
            Bug("System.IO.IOException", ProfilesLocked("vipig"), Now.AddHours(-1), "vipig", "PC-B"),
            Bug("System.IO.IOException", ProfilesLocked("Eugene"), Now.AddMinutes(-5), "Eugene", "PC-C")
        };

        var issues = SentryIssueBuilder.Build(bugs, new SentryTimeRange(Now.AddDays(-1), Now), 180);

        Assert.That(issues, Has.Count.EqualTo(1));
        var issue = issues[0];
        Assert.Multiple(() =>
        {
            Assert.That(issue.Count, Is.EqualTo(4));
            Assert.That(issue.CountUsers, Is.EqualTo(3));
            Assert.That(issue.FirstSeen, Is.EqualTo(Now.AddHours(-3)));
            Assert.That(issue.LastSeen, Is.EqualTo(Now.AddMinutes(-5)));
            Assert.That(issue.Users[0].Username, Is.EqualTo("yoko"));
            Assert.That(issue.Users[0].Count, Is.EqualTo(2));
            Assert.That(issue.LatestEvent.Username, Is.EqualTo("Eugene"));
        });
    }

    [Test]
    public void BuildIssues_SortedByCountThenRecency()
    {
        var bugs = new List<IBugInfo>
        {
            Bug("A", "rare", Now.AddHours(-1)),
            Bug("B", "often", Now.AddHours(-5)),
            Bug("B", "often", Now.AddHours(-4)),
            Bug("C", "rare too", Now.AddMinutes(-1))
        };

        var issues = SentryIssueBuilder.Build(bugs, new SentryTimeRange(Now.AddDays(-1), Now), 0);

        Assert.That(issues.Select(i => i.Exception), Is.EqualTo(new[] { "B", "C", "A" }));
    }

    [Test]
    public void BuildIssues_OutsideRangeOrWithoutExceptions_Skipped()
    {
        var bugs = new List<IBugInfo>
        {
            Bug("A", "in range", Now.AddHours(-1)),
            Bug("A", "too old", Now.AddDays(-3)),
            Bug("A", "no exceptions", Now.AddHours(-1), exceptions: [])
        };

        var issues = SentryIssueBuilder.Build(bugs, new SentryTimeRange(Now.AddDays(-1), Now), 0);

        Assert.That(issues.Select(i => i.Message), Is.EqualTo(new[] { "in range" }));
    }

    [Test]
    public void BuildIssues_CountsReleases()
    {
        var bugs = new List<IBugInfo>
        {
            Bug("A", "x", Now.AddHours(-3), release: "Gml.Launcher@1.0.0.14"),
            Bug("A", "x", Now.AddHours(-2), release: "Gml.Launcher@1.0.0.15"),
            Bug("A", "x", Now.AddHours(-1), release: "Gml.Launcher@1.0.0.15")
        };

        var issue = SentryIssueBuilder.Build(bugs, new SentryTimeRange(Now.AddDays(-1), Now), 0)[0];

        Assert.That(issue.Releases.Select(r => (r.Name, r.Count)),
            Is.EqualTo(new[] { ("Gml.Launcher@1.0.0.15", 2), ("Gml.Launcher@1.0.0.14", 1) }));
    }

    [Test]
    public void BuildIssues_GraphicUsesHoursForShortRanges_DaysForLonger()
    {
        var bugs = new List<IBugInfo> { Bug("A", "x", Now.AddMinutes(-30)) };

        var hourly = SentryIssueBuilder.Build(bugs, new SentryTimeRange(Now.AddHours(-24), Now), 0)[0];
        var daily = SentryIssueBuilder.Build(bugs, new SentryTimeRange(Now.AddDays(-30), Now), 0)[0];

        Assert.Multiple(() =>
        {
            Assert.That(hourly.Granularity, Is.EqualTo("hour"));
            Assert.That(hourly.Graphic, Has.Count.EqualTo(25));
            Assert.That(hourly.Graphic.Sum(b => b.Count), Is.EqualTo(1));
            Assert.That(daily.Granularity, Is.EqualTo("day"));
            Assert.That(daily.Graphic, Has.Count.EqualTo(31));
            Assert.That(daily.Graphic[^1], Is.EqualTo(new SentryBucketDto("2026-11-20", 1)));
        });
    }

    #endregion

    #region Stack traces

    [Test]
    public void FormatStackTrace_OutermostFirst_CrashFrameFirst_NoGluedMessages()
    {
        var exceptions = new List<IExceptionReport>
        {
            new ExceptionReport
            {
                Type = "System.IO.IOException", ValueData = "file is locked",
                StackTrace = new List<IStackTrace>
                {
                    new StackTrace { Function = "Caller()", Filename = "A.cs", Lineno = 1, InApp = true },
                    new StackTrace { Function = "Crash()", Filename = "B.cs", Lineno = 2, InApp = true }
                }
            },
            new ExceptionReport
            {
                Type = "System.AggregateException", ValueData = "One or more errors occurred.",
                StackTrace = new List<IStackTrace>()
            }
        };

        var text = SentryStackTraceFormatter.Format(exceptions);

        Assert.That(text, Is.EqualTo(
            "System.AggregateException: One or more errors occurred." + "\n" +
            " ---> System.IO.IOException: file is locked" + "\n" +
            "   at Crash() in B.cs:line 2" + "\n" +
            "   at Caller() in A.cs:line 1"));
    }

    [Test]
    public void BuildExceptions_DuplicatedFramesFromOldReports_ShownOnce()
    {
        var frames = new List<IStackTrace> { new StackTrace { Function = "Crash()", Filename = "B.cs", Lineno = 2 } };
        var exceptions = new List<IExceptionReport>
        {
            new ExceptionReport { Type = "Inner", ValueData = "a", StackTrace = frames },
            new ExceptionReport { Type = "Outer", ValueData = "b", StackTrace = frames }
        };

        var dto = SentryStackTraceFormatter.BuildExceptions(exceptions);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Select(e => e.Type), Is.EqualTo(new[] { "Outer", "Inner" }));
            Assert.That(dto[0].Frames, Is.Empty);
            Assert.That(dto[1].Frames, Has.Count.EqualTo(1));
        });
    }

    #endregion

    #region Summary and daily stats

    [Test]
    public void BuildSummary_UsesViewerDayAndRollingWeeks()
    {
        // Viewer is UTC+3: 22:30 UTC on the 19th is already the 20th for them.
        var bugs = new List<IBugInfo>
        {
            Bug("A", "x", new DateTime(2026, 11, 19, 22, 30, 0, DateTimeKind.Utc), "u1", "p1"),
            Bug("A", "x", new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc), "u2", "p2"),
            Bug("B", "y", new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc), "u1", "p1"),
            Bug("C", "z", Now.AddDays(-10), "u3", "p3")
        };

        var summary = SentryStatsBuilder.BuildSummary(bugs, totalBugs: 1317, Now, tzOffsetMinutes: 180);

        Assert.Multiple(() =>
        {
            Assert.That(summary.TotalBugs, Is.EqualTo(1317));
            Assert.That(summary.BugsToday, Is.EqualTo(2));
            Assert.That(summary.BugsYesterday, Is.EqualTo(1));
            Assert.That(summary.BugsLast7Days, Is.EqualTo(3));
            Assert.That(summary.BugsPrevious7Days, Is.EqualTo(1));
            Assert.That(summary.IssuesLast7Days, Is.EqualTo(2));
            Assert.That(summary.UsersLast7Days, Is.EqualTo(2));
            Assert.That(summary.LastBugAt, Is.EqualTo(new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public void BuildDaily_FillsEveryDayAndSplitsProjects()
    {
        var bugs = new List<IBugInfo>
        {
            Bug("A", "x", Now.AddHours(-1)),
            Bug("A", "x", Now.AddHours(-2), project: ProjectType.Backend),
            Bug("A", "x", Now.AddDays(-2))
        };

        var daily = SentryStatsBuilder.BuildDaily(bugs, Now, tzOffsetMinutes: 0, days: 7);

        Assert.Multiple(() =>
        {
            Assert.That(daily, Has.Count.EqualTo(7));
            Assert.That(daily[^1], Is.EqualTo(new SentryDailyStatsDto("2026-11-20", 1, 1)));
            Assert.That(daily[^3], Is.EqualTo(new SentryDailyStatsDto("2026-11-18", 1, 0)));
            Assert.That(daily[0].Date, Is.EqualTo("2026-11-14"));
        });
    }

    #endregion

    #region Envelope

    private const string EnvelopeHeader = "{\"event_id\":\"e1\",\"sent_at\":\"2026-11-20T11:00:05Z\"}";

    private static string EventPayload(string release = "Gml.Launcher@1.0.0.15") =>
        "{\"event_id\":\"e1\",\"timestamp\":\"2026-11-20T11:00:00Z\",\"server_name\":\"PC-A\",\"release\":\"" + release + "\"," +
        "\"user\":{\"username\":\"yoko\",\"ip_address\":\"{{auto}}\"}," +
        "\"contexts\":{\"os\":{\"raw_description\":\"Windows 11\"},\"runtime\":{\"type\":\"runtime\"}}," +
        "\"exception\":{\"values\":[" +
        "{\"type\":\"System.IO.IOException\",\"value\":\"inner\",\"stacktrace\":{\"frames\":[{\"function\":\"Inner()\"}]}}," +
        "{\"type\":\"System.AggregateException\",\"value\":\"outer\",\"stacktrace\":{\"frames\":[{\"function\":\"Outer()\"}]}}" +
        "]}}";

    [Test]
    public void ReadEnvelope_Event_KeepsFramesPerException()
    {
        var envelope = string.Join('\n', EnvelopeHeader, "{\"type\":\"event\",\"length\":10}", EventPayload());

        var bug = SentryEnvelopeReader.Read(envelope, "1.2.3.4");

        Assert.That(bug, Is.Not.Null);
        var exceptions = bug!.Exceptions.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(exceptions.Select(e => e.StackTrace.Single().Function), Is.EqualTo(new[] { "Inner()", "Outer()" }));
            Assert.That(bug.Release, Is.EqualTo("Gml.Launcher@1.0.0.15"));
            Assert.That(bug.IpAddress, Is.EqualTo("1.2.3.4"));
            Assert.That(bug.PcName, Is.EqualTo("PC-A"));
            Assert.That(SentryTime.ToUtc(bug.SendAt), Is.EqualTo(new DateTime(2026, 11, 20, 11, 0, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public void ReadEnvelope_TransactionOrSession_Ignored()
    {
        var transaction = string.Join('\n', EnvelopeHeader, "{\"type\":\"transaction\"}", "{\"type\":\"transaction\",\"spans\":[]}");
        var session = string.Join('\n', EnvelopeHeader, "{\"type\":\"session\"}", "{\"sid\":\"x\",\"status\":\"ok\"}");

        Assert.Multiple(() =>
        {
            Assert.That(SentryEnvelopeReader.Read(transaction, null), Is.Null);
            Assert.That(SentryEnvelopeReader.Read(session, null), Is.Null);
            Assert.That(SentryEnvelopeReader.Read("garbage", null), Is.Null);
        });
    }

    [Test]
    public void ReadEnvelope_EventWithoutException_Ignored()
    {
        var message = string.Join('\n', EnvelopeHeader, "{\"type\":\"event\"}", "{\"event_id\":\"e1\",\"message\":\"hi\"}");

        Assert.That(SentryEnvelopeReader.Read(message, null), Is.Null);
    }

    #endregion
}
