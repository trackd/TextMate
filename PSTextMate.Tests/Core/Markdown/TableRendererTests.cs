using System.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Extensions.Tables;
using PwshSpectreConsole.TextMate.Core.Markdown.Renderers;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PwshSpectreConsole.TextMate.Tests.Core.Markdown;

public class TableRendererTests
{
    [Fact]
    public void ExtractTableDataOptimized_DoesNotDuplicateHeaders()
    {
        // Arrange: simple markdown table
        var markdown = "| Name | Value |\n| ---- | ----- |\n| Alpha | 1 |\n| Beta | 2 |";
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var document = Markdig.Markdown.Parse(markdown, pipeline);
        var table = document.OfType<Markdig.Extensions.Tables.Table>().FirstOrDefault();
        var (_, theme) = PwshSpectreConsole.TextMate.Infrastructure.CacheManager.GetCachedTheme(ThemeName.DarkPlus);

        // Act
        var rows = TableRenderer.ExtractTableDataOptimized(table ?? throw new InvalidOperationException("table not found"), theme);

        // Assert
        // First row should be header, and should not appear twice in data rows
        rows.Should().NotBeNull();
        rows.Should().HaveCount(3); // header + 2 data rows
        rows[0].isHeader.Should().BeTrue();
        rows.Skip(1).All(r => !r.isHeader).Should().BeTrue();
    }
}
