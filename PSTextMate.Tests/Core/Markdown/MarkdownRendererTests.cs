using PwshSpectreConsole.TextMate.Core.Markdown.Renderers;
using TextMateSharp.Grammars;

namespace PwshSpectreConsole.TextMate.Tests.Core.Markdown;

public class MarkdownRendererTests
{
    [Fact]
    public void Render_SimpleMarkdown_ReturnsValidRows()
    {
        // Arrange
        var markdown = "# Hello World\nThis is a test.";
        var theme = CreateTestTheme();
        var themeName = ThemeName.DarkPlus;

        // Act
        var result = PwshSpectreConsole.TextMate.Core.Markdown.MarkdownRenderer.Render(markdown, theme, themeName);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Render_EmptyMarkdown_ReturnsEmptyRows()
    {
        // Arrange
        var markdown = "";
        var theme = CreateTestTheme();
        var themeName = ThemeName.DarkPlus;

        // Act
        var result = PwshSpectreConsole.TextMate.Core.Markdown.MarkdownRenderer.Render(markdown, theme, themeName);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void Render_CodeBlock_ProducesCodeBlockRenderer()
    {
        // Arrange
        var markdown = "```csharp\nvar x = 1;\n```";
        var theme = CreateTestTheme();
        var themeName = ThemeName.DarkPlus;

        // Act
        var result = PwshSpectreConsole.TextMate.Core.Markdown.MarkdownRenderer.Render(markdown, theme, themeName);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        // Additional assertions for code block rendering can be added
    }

    [Theory]
    [InlineData("# Heading 1")]
    [InlineData("## Heading 2")]
    [InlineData("### Heading 3")]
    public void Render_Headings_HandlesAllLevels(string markdownHeading)
    {
        // Arrange
        var theme = CreateTestTheme();
        var themeName = ThemeName.DarkPlus;

        // Act
        var result = PwshSpectreConsole.TextMate.Core.Markdown.MarkdownRenderer.Render(markdownHeading, theme, themeName);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
    }

    private static Theme CreateTestTheme()
    {
        // Use the internal CacheManager to get a cached Theme instance for tests
        var (registry, theme) = PwshSpectreConsole.TextMate.Infrastructure.CacheManager.GetCachedTheme(ThemeName.DarkPlus);
        return theme;
    }
}
