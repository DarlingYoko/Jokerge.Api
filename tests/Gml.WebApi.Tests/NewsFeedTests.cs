using Gml.Core.News;
using Gml.Dto.News;
using Gml.Web.Api.Core.Services;
using GmlCore.Interfaces.Enums;

namespace Gml.WebApi.Tests;

/// <summary>
///     The launcher renders news content as HTML and has no idea about panel news, so the server
///     turns the admin's Markdown into HTML and mixes panel posts into the existing news feed.
/// </summary>
public class NewsFeedTests
{
    private static PanelNewsItem Panel(string title, DateTimeOffset date, string content = "text",
        string? profile = null, string? version = null) => new()
    {
        Id = 1, Title = title, Content = content, ProfileName = profile, BuildVersion = version, Date = date
    };

    [Test]
    public void RenderPanelContent_ConvertsMarkdownToHtml()
    {
        var html = NewsFeed.RenderPanelContent(Panel("t", DateTimeOffset.UtcNow, "**bold**\n\n- one\n- two"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<strong>bold</strong>"));
            Assert.That(html, Does.Contain("<li>one</li>"));
        });
    }

    [Test]
    public void RenderPanelContent_EscapesRawHtml()
    {
        var html = NewsFeed.RenderPanelContent(Panel("t", DateTimeOffset.UtcNow, "<script>alert(1)</script>"));

        Assert.That(html, Does.Not.Contain("<script>"));
    }

    [Test]
    public void RenderPanelContent_ShowsProfileAndVersionBinding()
    {
        var html = NewsFeed.RenderPanelContent(
            Panel("t", DateTimeOffset.UtcNow, profile: "Survival<b>", version: "1.2.0"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("Survival&lt;b&gt;"));
            Assert.That(html, Does.Contain("1.2.0"));
        });
    }

    [Test]
    public void RenderPanelContent_NoBinding_HasNoBindingLine()
    {
        var html = NewsFeed.RenderPanelContent(Panel("t", DateTimeOffset.UtcNow, "plain"));

        Assert.That(html, Does.Not.Contain(NewsFeed.ProfileLabel));
    }

    [Test]
    public void Merge_PutsNewestFirstAcrossSources()
    {
        var now = DateTimeOffset.UtcNow;
        var external = new List<NewsReadDto>
        {
            new() { Title = "vk-old", Date = now.AddDays(-3), Type = NewsListenerType.VK },
            new() { Title = "vk-new", Date = now.AddHours(-1), Type = NewsListenerType.VK }
        };
        var panel = new[] { Panel("panel-mid", now.AddDays(-1)), Panel("panel-newest", now) };

        var feed = NewsFeed.Merge(external, panel);

        Assert.Multiple(() =>
        {
            Assert.That(feed.Select(n => n.Title),
                Is.EqualTo(new[] { "panel-newest", "vk-new", "panel-mid", "vk-old" }));
            Assert.That(feed[0].Type, Is.EqualTo(NewsListenerType.Panel));
        });
    }

    [Test]
    public void Merge_CutsToLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var panel = Enumerable.Range(0, 5).Select(i => Panel($"n{i}", now.AddMinutes(-i)));

        var feed = NewsFeed.Merge([], panel, limit: 3);

        Assert.That(feed.Select(n => n.Title), Is.EqualTo(new[] { "n0", "n1", "n2" }));
    }
}
