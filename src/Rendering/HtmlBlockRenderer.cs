using Markdig.Syntax;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;
using PSTextMate.Core;

namespace PSTextMate.Rendering;

/// <summary>
/// Renders HTML blocks with syntax highlighting.
/// </summary>
internal static class HtmlBlockRenderer {
    /// <summary>
    /// Renders an HTML block with syntax highlighting when possible.
    /// </summary>
    /// <param name="htmlBlock">The HTML block to render</param>
    /// <param name="theme">Theme for styling</param>
    /// <param name="themeName">Theme name for TextMateProcessor</param>
    /// <returns>Rendered HTML block in a panel</returns>
    public static IRenderable Render(HtmlBlock htmlBlock, Theme theme, ThemeName themeName) {
        List<string> htmlLines = ExtractHtmlLines(htmlBlock);

        // Try to render with HTML syntax highlighting
        try {
            IRenderable[]? htmlRenderables = TextMateProcessor.ProcessLinesCodeBlock([.. htmlLines], themeName, "html", false);
            if (htmlRenderables is not null) {
                return new Panel(new Rows(htmlRenderables))
                    .Border(BoxBorder.Rounded)
                    .Header("html", Justify.Left);
            }
        }
        catch {
            // Fallback to plain rendering
        }

        // Fallback: plain HTML panel
        return CreateFallbackHtmlPanel(htmlLines);
    }

    /// <summary>
    /// Extracts HTML lines from the HTML block.
    /// </summary>
    private static List<string> ExtractHtmlLines(HtmlBlock htmlBlock) {
        var htmlLines = new List<string>();

        for (int i = 0; i < htmlBlock.Lines.Count; i++) {
            Markdig.Helpers.StringLine line = htmlBlock.Lines.Lines[i];
            htmlLines.Add(line.Slice.ToString());
        }

        return htmlLines;
    }

    /// <summary>
    /// Creates a fallback HTML panel when syntax highlighting fails.
    /// </summary>
    private static Panel CreateFallbackHtmlPanel(List<string> htmlLines) {
        string htmlText = string.Join("\n", htmlLines);
        var text = new Text(htmlText, Style.Plain);
        return new Panel(text)
            .Border(BoxBorder.Rounded)
            .Header("html", Justify.Left);
    }
}
