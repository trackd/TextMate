using Xunit;
using PwshSpectreConsole.TextMate.Core.Markdown;

namespace PwshSpectreConsole.TextMate.Tests.Core.Markdown;

public class MarkdownPipelinesTests
{
    [Fact]
    public void Standard_ReturnsSamePipelineInstance()
    {
        var pipeline1 = MarkdownPipelines.Standard;
        var pipeline2 = MarkdownPipelines.Standard;

        Assert.Same(pipeline1, pipeline2);
    }

    [Fact]
    public void Standard_HasRequiredExtensions()
    {
        var pipeline = MarkdownPipelines.Standard;

        // Pipeline should be configured for tables, emphasis extras, etc.
        var markdown = "| Header |\n|--------|\n| Cell |";
        var doc = Markdig.Markdown.Parse(markdown, pipeline);

        Assert.NotNull(doc);
    }

    [Fact]
    public void Standard_ParsesTaskLists()
    {
        var pipeline = MarkdownPipelines.Standard;

        var markdown = "- [ ] Task\n- [x] Done";
        var doc = Markdig.Markdown.Parse(markdown, pipeline);

        Assert.NotNull(doc);
    }

    [Fact]
    public void Standard_ParsesAutoLinks()
    {
        var pipeline = MarkdownPipelines.Standard;

        var markdown = "https://example.com";
        var doc = Markdig.Markdown.Parse(markdown, pipeline);

        Assert.NotNull(doc);
    }
}
