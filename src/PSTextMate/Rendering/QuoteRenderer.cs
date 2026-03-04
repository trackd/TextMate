using Markdig.Syntax;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Themes;

namespace PSTextMate.Rendering;

/// <summary>
/// Renders markdown quote blocks.
/// </summary>
internal static class QuoteRenderer {
    /// <summary>
    /// Renders a quote block with a bordered panel.
    /// </summary>
    /// <param name="quote">The quote block to render</param>
    /// <param name="theme">Theme for styling</param>
    /// <returns>Rendered quote in a bordered panel</returns>
    public static IRenderable Render(QuoteBlock quote, Theme theme) {
        var rows = new List<IRenderable>();
        bool needsGap = false;

        foreach (Block subBlock in quote) {
            if (needsGap) {
                rows.Add(Text.Empty);
            }

            if (subBlock is ParagraphBlock paragraph) {
                rows.AddRange(ParagraphRenderer.Render(paragraph, theme, splitOnLineBreaks: true));
            }
            else {
                rows.Add(new Text(subBlock.ToString() ?? string.Empty, Style.Plain));
            }

            needsGap = true;
        }

        IRenderable content = rows.Count switch {
            0 => Text.Empty,
            1 => rows[0],
            _ => new Rows(rows)
        };

        return new Panel(content)
            .Border(BoxBorder.Heavy)
            .Header("quote", Justify.Left);
    }
}
