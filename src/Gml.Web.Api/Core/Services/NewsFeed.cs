using System.Net;
using System.Text;
using Gml.Dto.News;
using GmlCore.Interfaces.Enums;
using GmlCore.Interfaces.News;
using Markdig;

namespace Gml.Web.Api.Core.Services;

/// <summary>
///     Builds the news feed the launcher reads: panel posts (Markdown) rendered to HTML and mixed
///     with the external listeners' news, newest first.
/// </summary>
public static class NewsFeed
{
    public const int MaxItems = 50;
    public const string ProfileLabel = "Сборка:";
    public const string VersionLabel = "Версия:";

    // Raw HTML in the Markdown is escaped, so a post can only produce what Markdown itself allows.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UsePipeTables()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    public static string RenderPanelContent(IPanelNews news)
    {
        var html = new StringBuilder();

        var binding = new List<string>();
        if (!string.IsNullOrWhiteSpace(news.ProfileName))
            binding.Add($"{ProfileLabel} {WebUtility.HtmlEncode(news.ProfileName)}");
        if (!string.IsNullOrWhiteSpace(news.BuildVersion))
            binding.Add($"{VersionLabel} {WebUtility.HtmlEncode(news.BuildVersion)}");

        if (binding.Count > 0)
            html.Append("<p style='font-size: 11px;'><b>")
                .Append(string.Join(" · ", binding))
                .Append("</b></p>");

        html.Append(Markdown.ToHtml(news.Content ?? string.Empty, Pipeline));

        return html.ToString();
    }

    public static NewsReadDto ToLauncherNews(IPanelNews news)
    {
        return new NewsReadDto
        {
            Title = news.Title,
            Content = RenderPanelContent(news),
            Date = news.Date,
            Type = NewsListenerType.Panel
        };
    }

    public static List<NewsReadDto> Merge(IEnumerable<NewsReadDto> external, IEnumerable<IPanelNews> panel,
        int limit = MaxItems)
    {
        return external
            .Concat(panel.Select(ToLauncherNews))
            .OrderByDescending(n => n.Date ?? DateTimeOffset.MinValue)
            .Take(limit)
            .ToList();
    }
}
