using System.Text;
using Markdig.Syntax.Inlines;

namespace PSTextMate.Utilities;

/// <summary>
/// Utility for extracting plain text from Markdig inline elements.
/// Consolidates text extraction logic used across multiple renderers.
/// </summary>
internal static class InlineTextExtractor {
    /// <summary>
    /// Recursively extracts plain text from inline elements.
    /// </summary>
    /// <param name="inline">The inline element to extract text from</param>
    /// <param name="builder">StringBuilder to append extracted text to</param>
    public static void ExtractText(Inline inline, StringBuilder builder) {
        switch (inline) {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                break;

            case ContainerInline container:
                foreach (Inline child in container) {
                    ExtractText(child, builder);
                }
                break;

            case LeafInline leaf when leaf is CodeInline code:
                builder.Append(code.Content);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Extracts all text from an inline container into a single string.
    /// </summary>
    /// <param name="inlineContainer">The container to extract from</param>
    /// <returns>Extracted plain text</returns>
    public static string ExtractAllText(ContainerInline? inlineContainer) {
        if (inlineContainer is null) {
            return string.Empty;
        }

        StringBuilder builder = StringBuilderPool.Rent();
        try {
            foreach (Inline inline in inlineContainer) {
                ExtractText(inline, builder);
            }
            return builder.ToString();
        }
        finally {
            StringBuilderPool.Return(builder);
        }
    }
}
