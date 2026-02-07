using Markdig;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PSTextMate.Rendering;

/// <summary>
/// Markdown renderer that builds Spectre.Console objects directly instead of markup strings.
/// This eliminates VT escaping issues and avoids double-parsing overhead for better performance.
/// </summary>
internal static class MarkdownRenderer {
    /// <summary>
    /// Cached Markdig pipeline with trivia tracking enabled.
    /// Pipelines are expensive to create, so we cache it as a static field for reuse.
    /// Thread-safe: Markdig pipelines are immutable once built.
    /// </summary>
    private static readonly MarkdownPipeline _pipeline = CreateMarkdownPipeline();

    /// <summary>
    /// Renders markdown content using Spectre.Console object building.
    /// This approach eliminates VT escaping issues and improves performance.
    /// </summary>
    /// <param name="markdown">Markdown text (can be multi-line)</param>
    /// <param name="theme">Theme object for styling</param>
    /// <param name="themeName">Theme name for TextMateProcessor</param>
    /// <returns>Array of renderables for Spectre.Console rendering</returns>
    public static IRenderable[] Render(string markdown, Theme theme, ThemeName themeName) {
        MarkdownDocument? document = Markdown.Parse(markdown, _pipeline);

        var rows = new List<IRenderable>();
        Block? previousBlock = null;

        for (int i = 0; i < document.Count; i++) {
            Block? block = document[i];

            // Skip redundant paragraph that Markdig sometimes produces on the same line as a table
            if (block is ParagraphBlock && i + 1 < document.Count) {
                Block nextBlock = document[i + 1];
                if (nextBlock is Markdig.Extensions.Tables.Table table && block.Line == table.Line) {
                    continue;
                }
            }

            // Calculate blank lines from source line numbers
            // This is more reliable than trivia since extensions break trivia tracking
            if (previousBlock is not null) {
                int previousEndLine = GetBlockEndLine(previousBlock, markdown);
                int gap = block.Line - previousEndLine - 1;
                for (int j = 0; j < gap; j++) {
                    rows.Add(new Rows(Text.Empty));
                }
            }

            // Render the block - returns IEnumerable<IRenderable>
            rows.AddRange(BlockRenderer.RenderBlock(block, theme, themeName));

            previousBlock = block;
        }
        return [.. rows];
    }

    /// <summary>
    /// Gets the ending line number of a block by counting newlines in the source span.
    /// </summary>
    private static int GetBlockEndLine(Block block, string markdown) {
        // For container blocks, recursively find the last child's end line
        if (block is ContainerBlock container && container.Count > 0) {
            return GetBlockEndLine(container[^1], markdown);
        }
        // For fenced code blocks: opening fence + content lines + closing fence
        if (block is FencedCodeBlock fenced && fenced.Lines.Count > 0) {
            return block.Line + fenced.Lines.Count + 1;
        }
        // Count newlines within the block's span (excluding the final newline which separates blocks)
        // The span typically includes the trailing newline, so we stop before Span.End
        int endPosition = Math.Min(block.Span.End - 1, markdown.Length - 1);
        int newlineCount = 0;
        for (int i = block.Span.Start; i <= endPosition; i++) {
            if (markdown[i] == '\n') {
                newlineCount++;
            }
        }
        return block.Line + newlineCount;
    }

    /// <summary>
    /// Returns true for blocks that render with visual borders and need padding.
    /// </summary>
    private static bool IsBorderedBlock(Block block) =>
        block is QuoteBlock or FencedCodeBlock or HtmlBlock or Markdig.Extensions.Tables.Table;

    /// <summary>
    /// Creates the Markdig pipeline with all necessary extensions and trivia tracking enabled.
    /// Pipeline follows Markdig's roundtrip parser design pattern - see:
    /// https://github.com/xoofx/markdig/blob/master/src/Markdig/Roundtrip.md
    /// </summary>
    /// <returns>Configured MarkdownPipeline with trivia tracking enabled</returns>
    private static MarkdownPipeline CreateMarkdownPipeline() {
        return new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseTaskLists()
            .UsePipeTables()
            .UseAutoLinks()
            .EnableTrackTrivia()
            .Build();
    }
}
