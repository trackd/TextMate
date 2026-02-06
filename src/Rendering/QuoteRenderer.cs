using System.Text;
using Markdig.Syntax;
using PSTextMate.Utilities;
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
        string quoteText = ExtractQuoteText(quote, theme);

        var text = new Text(quoteText, Style.Plain);
        return new Panel(text)
            .Border(BoxBorder.Heavy)
            .Header("quote", Justify.Left);
    }

    /// <summary>
    /// Extracts text content from all blocks within the quote.
    /// </summary>
    private static string ExtractQuoteText(QuoteBlock quote, Theme theme) {
        string quoteText = string.Empty;
        bool isFirstParagraph = true;

        foreach (Block subBlock in quote) {
            if (subBlock is ParagraphBlock para) {
                // Add newline between multiple paragraphs
                if (!isFirstParagraph)
                    quoteText += "\n";

                StringBuilder quoteBuilder = StringBuilderPool.Rent();
                try {
                    if (para.Inline is not null) {
                        InlineTextExtractor.ExtractText(para.Inline, quoteBuilder);
                    }
                    quoteText += quoteBuilder.ToString();
                }
                finally {
                    StringBuilderPool.Return(quoteBuilder);
                }

                isFirstParagraph = false;
            }
            else {
                quoteText += subBlock.ToString();
            }
        }

        // Trim trailing whitespace/newlines
        quoteText = quoteText.TrimEnd();

        return quoteText;
    }
}
