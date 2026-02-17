namespace PSTextMate.Core;

/// <summary>
/// Maps Markdig markdown element types to TextMate scopes for theme lookup.
/// </summary>
internal static class MarkdigTextMateScopeMapper {
    private static readonly Dictionary<string, string[]> BlockScopeMap = new()
    {
        { "Heading1", new[] { "markup.heading.1.markdown", "markup.heading.markdown" } },
        { "Heading2", new[] { "markup.heading.2.markdown", "markup.heading.markdown" } },
        { "Heading3", new[] { "markup.heading.3.markdown", "markup.heading.markdown" } },
        { "Heading4", new[] { "markup.heading.4.markdown", "markup.heading.markdown" } },
        { "Heading5", new[] { "markup.heading.5.markdown", "markup.heading.markdown" } },
        { "Heading6", new[] { "markup.heading.6.markdown", "markup.heading.markdown" } },
        { "Paragraph", new[] { "markup.paragraph.markdown", "text.plain" } },
        { "List", new[] { "markup.list.markdown" } },
        { "ListItem", new[] { "markup.list.markdown" } },
        { "Table", new[] { "markup.table.markdown" } },
        { "TableRow", new[] { "markup.table.row.markdown" } },
        { "TableCell", new[] { "markup.table.cell.markdown" } },
        { "Quote", new[] { "markup.quote.markdown" } },
        { "ThematicBreak", new[] { "meta.separator.markdown" } },
        { "CodeBlock", new[] { "markup.raw.block.markdown" } },
        { "HtmlBlock", new[] { "markup.raw.block.html.markdown" } },
        { "TaskList", new[] { "markup.list.task.markdown" } },
    };

    private static readonly Dictionary<string, string[]> InlineScopeMap = new()
    {
        { "EmphasisItalic", new[] { "markup.italic.markdown" } },
        { "EmphasisBold", new[] { "markup.bold.markdown" } },
        { "EmphasisBoldItalic", new[] { "markup.bold.markdown", "markup.italic.markdown" } },
        { "Link", new[] { "markup.underline.link.markdown" } },
        { "Image", new[] { "markup.underline.link.image.markdown" } },
        { "CodeInline", new[] { "markup.inline.raw.markdown" } },
        { "Literal", new[] { "text.plain" } },
        { "LineBreak", new[] { "text.whitespace" } },
    };

    public static string[] GetBlockScopes(string blockType, int headingLevel = 0) {
        return blockType == "Heading" && headingLevel > 0 && headingLevel <= 6
            ? BlockScopeMap[$"Heading{headingLevel}"]
            : BlockScopeMap.TryGetValue(blockType, out string[]? scopes) ? scopes : ["text.plain"];
    }

    public static string[] GetInlineScopes(string inlineType, int emphasisLevel = 0) {
        return inlineType == "Emphasis"
            ? emphasisLevel switch {
                1 => InlineScopeMap["EmphasisItalic"],
                2 => InlineScopeMap["EmphasisBold"],
                3 => InlineScopeMap["EmphasisBoldItalic"],
                _ => ["text.plain"]
            }
            : InlineScopeMap.TryGetValue(inlineType, out string[]? scopes) ? scopes : ["text.plain"];
    }
}
