namespace PSTextMate.Utilities;

internal static class SpectreStyleCompat {
    public static Style Create(Color? foreground = null, Color? background = null, Decoration? decoration = null)
        => new(foreground, background, decoration);

    public static Style CreateWithLink(Color? foreground, Color? background, Decoration? decoration, string? link) {
        return string.IsNullOrWhiteSpace(link)
            ? new Style(foreground, background, decoration)
            : new Style(foreground, background, decoration, link);
    }

    public static string ToMarkup(Style? style) {
        if (style is null) {
            return string.Empty;
        }

        Style resolved = style ?? Style.Plain;
        return resolved.ToMarkup();
    }

    public static Style Resolve(Style? style) => style ?? Style.Plain;

    public static void Append(Paragraph paragraph, string text, Style? style = null, string? link = null) {
        ArgumentNullException.ThrowIfNull(paragraph);

        if (string.IsNullOrWhiteSpace(link)) {
            paragraph.Append(text, style);
            return;
        }

        Style baseStyle = Resolve(style);
        Style linked = CreateWithLink(baseStyle.Foreground, baseStyle.Background, baseStyle.Decoration, link);
        paragraph.Append(text, linked);
    }
}
