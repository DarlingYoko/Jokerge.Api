using Gml.Core.Launcher;
using Gml.Core.Services.Storage;
using Gml.Models.User;
using Gml.Web.Api.Core.Diagnostics;

namespace Gml.WebApi.Tests;

/// <summary>
///     The launcher's «Диагностика» button sends a report authorised by the player's access token;
///     the panel lists reports with the player's nickname and a summary of what looks wrong.
/// </summary>
public class DiagnosticReportServiceTests
{
    private string _baseDirectory = null!;
    private DiagnosticReportService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), "gml-diag-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDirectory);
        var storage = new SqliteDiagnosticReportStorage(new GmlSettings("GmlServer",
            "gfweagertghuysergfbsuyerbgiuyserg", _baseDirectory, new HttpClient()));
        _service = new DiagnosticReportService(storage);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_baseDirectory, true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file on Windows; a leftover temp folder is harmless.
        }
    }

    private static User Player(string name = "yoko", string token = "player-token-123", bool banned = false) =>
        new() { Name = name, AccessToken = token, IsBanned = banned };

    private static DiagnosticReportDto Report(string? text = null) => new()
    {
        LauncherVersion = "1.0.0.15",
        Os = new DiagnosticOsDto { Description = "Windows 11", Architecture = "X64", Type = "Windows" },
        Memory = new DiagnosticMemoryDto { TotalMb = 16384, FreeMb = 4096 },
        Disk = new DiagnosticDiskDto { GameFolder = @"C:\Users\yoko\AppData\Roaming\jokerge", FreeBytes = 50L << 30, TotalBytes = 500L << 30 },
        Java = new DiagnosticJavaDto { Found = true, Path = "runtime/java-21/bin/java.exe", Version = "21" },
        Connectivity =
        [
            new DiagnosticConnectivityDto { Name = "api", Target = "https://launcher.jokerge.com", Reachable = true, LatencyMs = 40, StatusCode = 200 },
            new DiagnosticConnectivityDto { Name = "files", Target = "https://launcher.jokerge.com", Reachable = false, Error = "timeout" }
        ],
        Profile = new DiagnosticProfileDto { Name = "Waffle", IsDynamicRam = false, RamMb = 6144, RecommendedRamMb = 4096, WindowWidth = 900, WindowHeight = 600 },
        LogTail = ["line 1", "Authorization: Bearer player-token-123"],
        CollectedAt = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc),
        Text = text ?? @"Папка: C:\Users\yoko\AppData token=player-token-123"
    };

    #region Authorization

    [TestCase("player-token-123", true)]
    [TestCase("Bearer player-token-123", true)]
    [TestCase("bearer player-token-123", true)]
    [TestCase("other", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsAuthorized_ChecksTheToken(string? header, bool expected)
    {
        Assert.That(DiagnosticReportService.IsAuthorized(Player(), header), Is.EqualTo(expected));
    }

    [Test]
    public void IsAuthorized_RejectsBannedAndUnknownPlayers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DiagnosticReportService.IsAuthorized(Player(banned: true), "player-token-123"), Is.False);
            Assert.That(DiagnosticReportService.IsAuthorized(null, "player-token-123"), Is.False);
            Assert.That(DiagnosticReportService.IsAuthorized(Player(token: ""), ""), Is.False);
        });
    }

    #endregion

    [Test]
    public async Task Submit_ThenGet_ReturnsReportUnderPlayersNickname()
    {
        var id = await _service.SubmitAsync(Player(), Report());

        var read = await _service.GetAsync(id);

        Assert.That(read, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(read!.Id, Is.EqualTo(id));
            Assert.That(read.UserName, Is.EqualTo("yoko"));
            Assert.That(read.Report.Profile.Name, Is.EqualTo("Waffle"));
            Assert.That(read.Report.Connectivity, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Submit_MasksTokenAndProfilePathsOnTheServerToo()
    {
        var id = await _service.SubmitAsync(Player(), Report());

        var read = (await _service.GetAsync(id))!;
        var everything = string.Join("\n", read.Report.Text, read.Report.Disk.GameFolder,
            string.Join("\n", read.Report.LogTail));

        Assert.Multiple(() =>
        {
            Assert.That(everything, Does.Not.Contain("player-token-123"));
            Assert.That(everything, Does.Not.Contain(@"Users\yoko"));
            Assert.That(read.Report.Disk.GameFolder, Is.EqualTo(@"C:\Users\<user>\AppData\Roaming\jokerge"));
        });
    }

    [Test]
    public async Task Submit_TrimsOversizedLogAndText()
    {
        var report = Report(new string('x', DiagnosticReportService.MaxTextLength + 500));
        report.LogTail = Enumerable.Range(0, DiagnosticReportService.MaxLogLines + 50)
            .Select(i => $"line {i} " + new string('y', DiagnosticReportService.MaxLineLength))
            .ToList();

        var read = (await _service.GetAsync(await _service.SubmitAsync(Player(), report)))!;

        Assert.Multiple(() =>
        {
            Assert.That(read.Report.LogTail, Has.Count.EqualTo(DiagnosticReportService.MaxLogLines));
            Assert.That(read.Report.LogTail[^1], Does.StartWith($"line {DiagnosticReportService.MaxLogLines + 49} "));
            Assert.That(read.Report.LogTail.Max(l => l.Length), Is.LessThanOrEqualTo(DiagnosticReportService.MaxLineLength));
            Assert.That(read.Report.Text.Length, Is.LessThanOrEqualTo(DiagnosticReportService.MaxTextLength));
        });
    }

    [Test]
    public async Task Page_HasSummaryForThePanel()
    {
        await _service.SubmitAsync(Player(), Report());

        var page = await _service.GetPageAsync(take: 20, offset: 0, userName: null);
        var summary = page.Items.Single();

        Assert.Multiple(() =>
        {
            Assert.That(page.Total, Is.EqualTo(1));
            Assert.That(summary.UserName, Is.EqualTo("yoko"));
            Assert.That(summary.LauncherVersion, Is.EqualTo("1.0.0.15"));
            Assert.That(summary.OsDescription, Is.EqualTo("Windows 11"));
            Assert.That(summary.Architecture, Is.EqualTo("X64"));
            Assert.That(summary.TotalRamMb, Is.EqualTo(16384));
            Assert.That(summary.FreeRamMb, Is.EqualTo(4096));
            Assert.That(summary.SelectedRamMb, Is.EqualTo(6144));
            Assert.That(summary.FreeDiskBytes, Is.EqualTo(50L << 30));
            Assert.That(summary.JavaFound, Is.True);
            Assert.That(summary.ApiReachable, Is.True);
            Assert.That(summary.FilesReachable, Is.False);
            Assert.That(summary.ProfileName, Is.EqualTo("Waffle"));
        });
    }

    [TestCase(0, 1)]
    [TestCase(1000, 100)]
    public async Task Page_ClampsTake(int take, int expectedMax)
    {
        for (var i = 0; i < 3; i++) await _service.SubmitAsync(Player("p" + i), Report());

        var page = await _service.GetPageAsync(take, 0, null);

        Assert.That(page.Items.Count, Is.LessThanOrEqualTo(expectedMax).And.GreaterThan(0));
    }

    [Test]
    public async Task Remove_DeletesReport()
    {
        var id = await _service.SubmitAsync(Player(), Report());

        Assert.That(await _service.RemoveAsync(id), Is.True);
        Assert.That(await _service.GetAsync(id), Is.Null);
    }
}
