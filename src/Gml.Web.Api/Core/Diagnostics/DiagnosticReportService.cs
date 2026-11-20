using System.Text.Json;
using GmlCore.Interfaces.Storage;
using GmlCore.Interfaces.User;

namespace Gml.Web.Api.Core.Diagnostics;

/// <summary>
///     Accepts diagnostics reports from the launcher and serves them to the panel. Everything the
///     player sent is trimmed and masked again here, so a launcher bug cannot leak a token into the panel.
/// </summary>
public class DiagnosticReportService(IDiagnosticReportStorage storage)
{
    public const int KeepPerUser = 20;
    public const int MaxLogLines = 200;
    public const int MaxLineLength = 1000;
    public const int MaxTextLength = 64 * 1024;
    public const int MaxFieldLength = 512;
    public const int MaxConnectivityChecks = 16;
    public const int MaxPageSize = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     The launcher sends the player's access token in <c>Authorization</c>, raw (as for
    ///     <c>/profiles/info</c>) or as <c>Bearer</c>.
    /// </summary>
    public static bool IsAuthorized(IUser? user, string? authorizationHeader)
    {
        if (user is null || user.IsBanned || string.IsNullOrEmpty(user.AccessToken) ||
            string.IsNullOrWhiteSpace(authorizationHeader))
            return false;

        var token = authorizationHeader.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token["Bearer ".Length..].Trim();

        return string.Equals(token, user.AccessToken, StringComparison.Ordinal);
    }

    public async Task<Guid> SubmitAsync(IUser user, DiagnosticReportDto report)
    {
        var clean = Sanitize(report, [user.AccessToken]);
        var id = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        var summary = BuildSummary(id, user.Name, createdAt, clean);

        await storage.AddDiagnosticReportAsync(
            new DiagnosticReportRecord(id, user.Name, createdAt,
                JsonSerializer.Serialize(summary, Json),
                JsonSerializer.Serialize(clean, Json)),
            KeepPerUser);

        return id;
    }

    public async Task<DiagnosticReportPageDto> GetPageAsync(int take, int offset, string? userName)
    {
        var page = await storage.GetDiagnosticReportsAsync(Math.Clamp(take, 1, MaxPageSize), Math.Max(0, offset),
            userName);

        return new DiagnosticReportPageDto
        {
            Total = page.Total,
            Items = page.Items.Select(row =>
            {
                var summary = JsonSerializer.Deserialize<DiagnosticReportSummaryDto>(row.Summary, Json)
                              ?? new DiagnosticReportSummaryDto();
                summary.Id = row.Id;
                summary.UserName = row.UserName;
                summary.CreatedAt = row.CreatedAt;
                return summary;
            }).ToList()
        };
    }

    public async Task<DiagnosticReportReadDto?> GetAsync(Guid id)
    {
        var record = await storage.GetDiagnosticReportAsync(id);
        if (record is null) return null;

        return new DiagnosticReportReadDto
        {
            Id = record.Id,
            UserName = record.UserName,
            CreatedAt = record.CreatedAt,
            Report = JsonSerializer.Deserialize<DiagnosticReportDto>(record.Payload, Json) ?? new DiagnosticReportDto()
        };
    }

    public Task<bool> RemoveAsync(Guid id) => storage.RemoveDiagnosticReportAsync(id);

    internal static DiagnosticReportSummaryDto BuildSummary(Guid id, string userName, DateTime createdAt,
        DiagnosticReportDto report) => new()
    {
        Id = id,
        UserName = userName,
        CreatedAt = createdAt,
        LauncherVersion = report.LauncherVersion,
        OsDescription = report.Os.Description,
        Architecture = report.Os.Architecture,
        TotalRamMb = report.Memory.TotalMb,
        FreeRamMb = report.Memory.FreeMb,
        SelectedRamMb = report.Profile.RamMb,
        FreeDiskBytes = report.Disk.FreeBytes,
        JavaFound = report.Java.Found,
        ApiReachable = IsReachable(report, "api"),
        FilesReachable = IsReachable(report, "files"),
        ProfileName = report.Profile.Name
    };

    private static bool IsReachable(DiagnosticReportDto report, string name) =>
        report.Connectivity.Any(c => c.Reachable && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    internal static DiagnosticReportDto Sanitize(DiagnosticReportDto report, IReadOnlyCollection<string?> secrets)
    {
        string Field(string? value) => Cut(DiagnosticTextRedactor.Redact(value, secrets), MaxFieldLength);
        string? OptionalField(string? value) => value is null ? null : Field(value);

        var os = report.Os ?? new DiagnosticOsDto();
        var memory = report.Memory ?? new DiagnosticMemoryDto();
        var disk = report.Disk ?? new DiagnosticDiskDto();
        var java = report.Java ?? new DiagnosticJavaDto();
        var profile = report.Profile ?? new DiagnosticProfileDto();

        return new DiagnosticReportDto
        {
            LauncherVersion = Field(report.LauncherVersion),
            Os = new DiagnosticOsDto
            {
                Description = Field(os.Description), Architecture = Field(os.Architecture), Type = Field(os.Type)
            },
            Memory = new DiagnosticMemoryDto { TotalMb = Math.Max(0, memory.TotalMb), FreeMb = Math.Max(0, memory.FreeMb) },
            Disk = new DiagnosticDiskDto
            {
                GameFolder = Field(disk.GameFolder),
                FreeBytes = Math.Max(0, disk.FreeBytes),
                TotalBytes = Math.Max(0, disk.TotalBytes)
            },
            Java = new DiagnosticJavaDto { Found = java.Found, Path = OptionalField(java.Path), Version = OptionalField(java.Version) },
            Connectivity = (report.Connectivity ?? [])
                .Where(c => c is not null)
                .Take(MaxConnectivityChecks)
                .Select(c => new DiagnosticConnectivityDto
                {
                    Name = Field(c.Name),
                    Target = Field(c.Target),
                    Reachable = c.Reachable,
                    LatencyMs = c.LatencyMs,
                    StatusCode = c.StatusCode,
                    Error = OptionalField(c.Error)
                })
                .ToList(),
            Profile = new DiagnosticProfileDto
            {
                Name = OptionalField(profile.Name),
                IsDynamicRam = profile.IsDynamicRam,
                RamMb = Math.Max(0, profile.RamMb),
                RecommendedRamMb = profile.RecommendedRamMb,
                FullScreen = profile.FullScreen,
                WindowWidth = profile.WindowWidth,
                WindowHeight = profile.WindowHeight
            },
            // Keep the newest lines: the end of a log is where the failure is.
            LogTail = (report.LogTail ?? [])
                .TakeLast(MaxLogLines)
                .Select(line => Cut(DiagnosticTextRedactor.Redact(line, secrets), MaxLineLength))
                .ToList(),
            CollectedAt = report.CollectedAt,
            Text = Cut(DiagnosticTextRedactor.Redact(report.Text, secrets), MaxTextLength)
        };
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
