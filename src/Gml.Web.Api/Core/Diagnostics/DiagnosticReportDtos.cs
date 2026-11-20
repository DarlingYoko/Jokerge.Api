namespace Gml.Web.Api.Core.Diagnostics;

/// <summary>What the launcher posts to <c>/api/v1/launcher/diagnostics</c>.</summary>
public class DiagnosticReportSubmitDto
{
    public string UserName { get; set; } = string.Empty;
    public DiagnosticReportDto? Report { get; set; }
}

/// <summary>The report itself, as collected by the launcher's «Диагностика» button.</summary>
public class DiagnosticReportDto
{
    public string LauncherVersion { get; set; } = string.Empty;
    public DiagnosticOsDto Os { get; set; } = new();
    public DiagnosticMemoryDto Memory { get; set; } = new();
    public DiagnosticDiskDto Disk { get; set; } = new();
    public DiagnosticJavaDto Java { get; set; } = new();
    public List<DiagnosticConnectivityDto> Connectivity { get; set; } = [];
    public DiagnosticProfileDto Profile { get; set; } = new();
    public List<string> LogTail { get; set; } = [];
    public DateTime CollectedAt { get; set; }

    /// <summary>The human-readable report exactly as the player sees and copies it.</summary>
    public string Text { get; set; } = string.Empty;
}

public class DiagnosticOsDto
{
    public string Description { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public class DiagnosticMemoryDto
{
    public long TotalMb { get; set; }
    public long FreeMb { get; set; }
}

public class DiagnosticDiskDto
{
    public string GameFolder { get; set; } = string.Empty;
    public long FreeBytes { get; set; }
    public long TotalBytes { get; set; }
}

public class DiagnosticJavaDto
{
    public bool Found { get; set; }
    public string? Path { get; set; }
    public string? Version { get; set; }
}

public class DiagnosticConnectivityDto
{
    /// <summary><c>api</c>, <c>files</c>, <c>ping</c>.</summary>
    public string Name { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;
    public bool Reachable { get; set; }
    public long? LatencyMs { get; set; }
    public int? StatusCode { get; set; }
    public string? Error { get; set; }
}

public class DiagnosticProfileDto
{
    public string? Name { get; set; }
    public bool IsDynamicRam { get; set; }
    public int RamMb { get; set; }
    public int? RecommendedRamMb { get; set; }
    public bool FullScreen { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
}

/// <summary>One row of the panel list; stored next to the report so the list never loads payloads.</summary>
public class DiagnosticReportSummaryDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string LauncherVersion { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public long TotalRamMb { get; set; }
    public long FreeRamMb { get; set; }
    public int SelectedRamMb { get; set; }
    public long FreeDiskBytes { get; set; }
    public bool JavaFound { get; set; }
    public bool ApiReachable { get; set; }
    public bool FilesReachable { get; set; }
    public string? ProfileName { get; set; }
}

public class DiagnosticReportPageDto
{
    public List<DiagnosticReportSummaryDto> Items { get; set; } = [];
    public int Total { get; set; }
}

public class DiagnosticReportReadDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DiagnosticReportDto Report { get; set; } = new();
}

public class DiagnosticReportCreatedDto
{
    public Guid Id { get; set; }
}
