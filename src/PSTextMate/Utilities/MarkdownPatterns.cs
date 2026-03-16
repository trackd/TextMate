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

        int significantCount = 0;
        LinkInline? candidate = null;

        foreach (Inline inline in paragraph.Inline) {
            if (inline is LineBreakInline) {
                continue;
            }

            if (inline is LiteralInline literal && IsWhitespaceLiteral(literal.Content)) {
                continue;
            }

            significantCount++;
            if (significantCount > 1) {
                return false;
            }

            candidate = inline as LinkInline;
        }

        return significantCount == 1 && candidate is { IsImage: true };
    }

    private static bool IsWhitespaceLiteral(StringSlice slice) {
        if (slice.Text is null || slice.Length <= 0) {
            return true;
        }

        foreach (char c in slice.Text.AsSpan(slice.Start, slice.Length)) {
            if (!char.IsWhiteSpace(c)) {
                return false;
            }
        }

        return true;
    }
}
