using System.Text;
using GmlCore.Interfaces.Launcher;
using GmlCore.Interfaces.Sentry;

namespace Gml.Web.Api.Core.Sentry;

/// <summary>
///     Turns Sentry's exception list into what a .NET developer expects to read. Sentry lists the
///     root cause first and frames caller-first; .NET prints the outer exception first and the
///     crashing frame first. The old code also glued every message together character by
///     character ("...another process.One or more errors occurred.").
/// </summary>
public static class SentryStackTraceFormatter
{
    public static IReadOnlyList<SentryExceptionDto> BuildExceptions(IEnumerable<IExceptionReport>? exceptions)
    {
        var rootFirst = (exceptions ?? []).ToList();
        var result = new List<SentryExceptionDto>(rootFirst.Count);

        // Reports stored before the envelope fix carry every exception's frames on each
        // exception. Keep each distinct frame list only once, on the innermost exception.
        IReadOnlyList<SentryFrameDto>? previousFrames = null;
        var frames = new List<IReadOnlyList<SentryFrameDto>>(rootFirst.Count);
        foreach (var exception in rootFirst)
        {
            var current = BuildFrames(exception.StackTrace);
            frames.Add(previousFrames is not null && current.Count > 0 && current.SequenceEqual(previousFrames)
                ? []
                : current);
            previousFrames = current;
        }

        for (var i = rootFirst.Count - 1; i >= 0; i--)
        {
            var exception = rootFirst[i];
            result.Add(new SentryExceptionDto(
                string.IsNullOrWhiteSpace(exception.Type) ? "Exception" : exception.Type,
                exception.ValueData ?? string.Empty,
                frames[i]));
        }

        return result;
    }

    public static string Format(IEnumerable<IExceptionReport>? exceptions) => Format(BuildExceptions(exceptions));

    public static string Format(IReadOnlyList<SentryExceptionDto> exceptions)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < exceptions.Count; i++)
        {
            var exception = exceptions[i];
            if (i > 0)
                builder.Append('\n').Append(" ---> ");

            builder.Append(exception.Type);
            if (!string.IsNullOrEmpty(exception.Message))
                builder.Append(": ").Append(exception.Message);

            foreach (var frame in exception.Frames)
                builder.Append('\n').Append(FormatFrame(frame));
        }

        return builder.ToString();
    }

    private static string FormatFrame(SentryFrameDto frame)
    {
        // Backend captures keep the whole .NET stack text in Function, already formatted.
        if (frame.File is null && frame.Function is not null && frame.Function.Contains('\n'))
            return frame.Function.TrimEnd();

        var line = new StringBuilder("   at ").Append(frame.Function ?? "?");
        if (!string.IsNullOrEmpty(frame.File))
        {
            line.Append(" in ").Append(frame.File);
            if (frame.Line is > 0)
                line.Append(":line ").Append(frame.Line);
        }

        return line.ToString();
    }

    private static IReadOnlyList<SentryFrameDto> BuildFrames(IEnumerable<IStackTrace>? frames)
    {
        if (frames is null)
            return [];

        var list = frames
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Function))
            .Select(f => new SentryFrameDto(
                f.Function,
                FirstNonEmpty(f.AbsPath, f.Filename),
                f.Lineno > 0 ? f.Lineno : null,
                f.InApp))
            .ToList();

        list.Reverse();
        return list;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && v != "Not Found");
}
