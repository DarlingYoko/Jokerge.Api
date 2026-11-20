using Gml.Core.Launcher;
using Gml.Dto.Sentry;
using GmlCore.Interfaces.Launcher;
using GmlCore.Interfaces.Sentry;
using Newtonsoft.Json;
using MemoryInfo = Gml.Core.Launcher.MemoryInfo;
using StackTrace = Gml.Core.Launcher.StackTrace;

namespace Gml.Web.Api.Core.Sentry;

/// <summary>
///     Reads the launcher's Sentry envelope into a bug report. The launcher runs Sentry with
///     tracing on, so most envelopes are transactions or sessions; those used to blow up the
///     endpoint with a null reference. Only events that carry an exception become reports.
/// </summary>
public static class SentryEnvelopeReader
{
    private const string NotFound = "Not found";

    public static BugInfo? Read(string envelope, string? remoteIp)
    {
        var lines = envelope.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3)
            return null;

        var header = TryParse<SentryEventDto>(lines[0]);

        // Items are header/payload line pairs; the event comes before any binary attachment.
        for (var i = 1; i + 1 < lines.Length; i += 2)
        {
            var item = TryParse<SentryEventLengthDto>(lines[i]);
            if (item is null)
                return null;

            if (!string.Equals(item.Type, "event", StringComparison.OrdinalIgnoreCase))
                continue;

            var payload = TryParse<SentryModulesDto>(lines[i + 1]);
            return payload is null ? null : ToBugInfo(payload, header, remoteIp);
        }

        return null;
    }

    private static BugInfo? ToBugInfo(SentryModulesDto payload, SentryEventDto? header, string? remoteIp)
    {
        var values = payload.Exception?.Values?.Where(v => v is not null).ToList();
        if (values is null || values.Count == 0)
            return null;

        var ip = payload.User?.IpAddress;
        if (string.IsNullOrWhiteSpace(ip) || ip == "{{auto}}")
            ip = remoteIp;

        var memory = payload.Contexts?.MemoryInfo;
        var sentAt = payload.Timestamp != default ? payload.Timestamp : header?.SentAt ?? DateTime.UtcNow;

        return new BugInfo
        {
            ProjectType = ProjectType.Launcher,
            PcName = payload.ServerName ?? NotFound,
            Username = payload.User?.Username ?? NotFound,
            MemoryInfo = new MemoryInfo
            {
                AllocatedBytes = memory?.AllocatedBytes ?? 0,
                HighMemoryLoadThresholdBytes = memory?.HighMemoryLoadThresholdBytes ?? 0,
                TotalAvailableMemoryBytes = memory?.TotalAvailableMemoryBytes ?? 0,
                Compacted = memory?.Compacted ?? false,
                Concurrent = memory?.Concurrent ?? false,
                PauseDurations = memory?.PauseDurations ?? []
            },
            Exceptions = values.Select(ToReport).ToList(),
            SendAt = SentryTime.ToUtc(sentAt),
            IpAddress = ip ?? NotFound,
            OsVersion = payload.Contexts?.Os?.RawDescription,
            OsIdentifier = payload.Contexts?.Runtime?.RawDescription ?? payload.Contexts?.Runtime?.Type,
            Release = payload.Release,
            Player = PlayerTag(payload.Tags)
        };
    }

    /// <summary>The launcher tags events with the signed-in Minecraft nickname as <c>player</c>.</summary>
    private static string? PlayerTag(IReadOnlyDictionary<string, string>? tags) =>
        tags is not null && tags.TryGetValue("player", out var player) && !string.IsNullOrWhiteSpace(player)
            ? player.Trim()
            : null;

    private static ExceptionReport ToReport(Value value) => new()
    {
        Type = value.Type ?? "Not Found",
        ValueData = value.ValueData ?? "Not Found",
        Module = value.Module ?? "Not Found",
        ThreadId = value.ThreadId,
        Id = value.Id,
        Crashed = value.Crashed,
        Current = value.Current,
        // Each exception keeps its own frames; they used to get every exception's frames.
        StackTrace = (value.Stacktrace?.Frames ?? [])
            .Where(frame => frame is not null)
            .Select(frame => (IStackTrace)new StackTrace
            {
                Filename = frame.Filename,
                Function = frame.Function,
                Lineno = frame.Lineno,
                Colno = frame.Colno,
                AbsPath = frame.AbsPath ?? "Not Found",
                InApp = frame.InApp,
                Package = frame.Package,
                InstructionAddr = frame.InstructionAddr,
                AddrMode = frame.AddrMode,
                FunctionId = frame.FunctionId
            })
            .ToList()
    };

    private static T? TryParse<T>(string json) where T : class
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
