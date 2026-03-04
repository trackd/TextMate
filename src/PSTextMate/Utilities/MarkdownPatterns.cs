using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PSTextMate.Utilities;

/// <summary>
/// Utility for detecting common markdown patterns like standalone images.
/// Consolidates pattern detection logic used across multiple renderers.
/// </summary>
internal static class MarkdownPatterns {
    /// <summary>
    /// Checks if a paragraph block contains only a single image (no other text).
    /// Used to apply special rendering or spacing for standalone images.
    /// </summary>
    /// <param name="paragraph">The paragraph block to check</param>
    /// <returns>True if paragraph contains only an image, false otherwise</returns>
    public static bool IsStandaloneImage(ParagraphBlock paragraph) {
        if (paragraph.Inline is null) {
            return false;
        }

        // Check if the paragraph contains only one LinkInline with IsImage = true
        var inlines = paragraph.Inline.ToList();

        // Single image case
        if (inlines.Count == 1 && inlines[0] is LinkInline link && link.IsImage) {
            return true;
        }

        // Sometimes there might be whitespace inlines around the image
        // Filter out empty/whitespace literals
        var nonWhitespace = inlines
            .Where(i => i is not LineBreakInline &&
                    !(i is LiteralInline lit && string.IsNullOrWhiteSpace(lit.Content.ToString())))
            .ToList();

        return nonWhitespace.Count == 1
                && nonWhitespace[0] is LinkInline imageLink
                && imageLink.IsImage;
    }
}
