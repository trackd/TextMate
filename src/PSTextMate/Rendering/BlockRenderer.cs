using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PSTextMate.Rendering;

/// <summary>
/// Block renderer that uses Spectre.Console object building instead of markup strings.
/// This eliminates VT escaping issues and improves performance by avoiding double-parsing.
/// </summary>
internal static class BlockRenderer {
    /// <summary>
    /// Routes block elements to their appropriate renderers.
    /// All renderers build Spectre.Console objects directly instead of markup strings.
    /// </summary>
    /// <param name="block">The block element to render</param>
    /// <param name="theme">Theme for styling</param>
    /// <param name="themeName">Theme name for TextMateProcessor</param>
    /// <returns>Enumerable of rendered items (each block produces one or more renderables)</returns>
    public static IEnumerable<IRenderable> RenderBlock(Block block, Theme theme, ThemeName themeName) {
        return block switch {
            // Special handling for paragraphs that contain only an image
            // Return the image renderable followed by an explicit blank row so the image
            // and the safety padding are separate renderables (not inside the same widget).
            ParagraphBlock paragraph when MarkdownPatterns.IsStandaloneImage(paragraph) =>
                RenderStandaloneImage(paragraph, theme) is IRenderable r ? new[] { r, Text.NewLine } : [],

            // Use renderers that build Spectre.Console objects directly
            HeadingBlock heading
                => [HeadingRenderer.Render(heading, theme)],
            ParagraphBlock paragraph
                => ParagraphRenderer.Render(paragraph, theme),  // Returns IEnumerable<IRenderable>
            ListBlock list
                => ListRenderer.Render(list, theme),
            Markdig.Extensions.Tables.Table table
                => TableRenderer.Render(table, theme) is IRenderable t ? [t] : [],
            FencedCodeBlock fencedCode
                => CodeBlockRenderer.RenderFencedCodeBlock(fencedCode, theme, themeName) is IRenderable fc ? [fc] : [],
            CodeBlock indentedCode
                => CodeBlockRenderer.RenderCodeBlock(indentedCode, theme) is IRenderable ic ? [ic] : [],

            // Keep existing renderers for remaining complex blocks
            QuoteBlock quote
                => [QuoteRenderer.Render(quote, theme)],
            HtmlBlock html
                => HtmlBlockRenderer.Render(html, theme, themeName) is IRenderable h ? [h] : [],
            ThematicBreakBlock
                => [HorizontalRuleRenderer.Render()],

            // Unsupported block types
            _ => []
        };
    }

    /// <summary>
    /// Renders a standalone image (paragraph containing only an image).
    /// Demonstrates how SixelImage can be directly rendered or wrapped in containers.
    /// </summary>
    private static IRenderable? RenderStandaloneImage(ParagraphBlock paragraph, Theme theme) {
        if (paragraph.Inline is null) {
            return null;
        }

        // Find the image link
        LinkInline? imageLink = paragraph.Inline
            .OfType<LinkInline>()
            .FirstOrDefault(link => link.IsImage);

        if (imageLink is null) {
            return null;
        }

        // Extract alt text
        string altText = ExtractImageAltText(imageLink);

        // Render using ImageBlockRenderer which handles various layouts
        // Can render as: Direct (most common), PanelWithCaption, WithPadding, etc.
        // This demonstrates how SixelImage (an IRenderable) can be embedded in different containers:
        // - Panel: Wrap with border and title
        // - Columns: Side-by-side layout
        // - Rows: Vertical stacking
        // - Grid: Flexible grid layout
        // - Table: Inside table cells
        // - Or rendered directly without wrapper

        return ImageBlockRenderer.RenderImageBlock(
            altText,
            imageLink.Url ?? "",
            renderMode: ImageRenderMode.Direct);  // Direct rendering is most efficient
    }

    /// <summary>
    /// Extracts alt text from an image link inline.
    /// </summary>
    private static string ExtractImageAltText(LinkInline imageLink) {
        var textBuilder = new System.Text.StringBuilder();

        foreach (Inline inline in imageLink) {
            if (inline is LiteralInline literal) {
                textBuilder.Append(literal.Content.ToString());
            }
            else if (inline is CodeInline code) {
                textBuilder.Append(code.Content);
            }
        }

        string result = textBuilder.ToString();
        return string.IsNullOrEmpty(result) ? "Image" : result;
    }
}
