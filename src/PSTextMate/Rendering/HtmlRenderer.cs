using System.Buffers;

namespace PSTextMate.Core;

/// <summary>Exports the shared presentation as escaped, styled HTML without UI dependencies.</summary>
public static class HtmlRenderer {
    private static readonly SearchValues<char> Escapes = SearchValues.Create("&<>\"'");

    public static string Format(HighlightedDocument document, bool fullDocument = false) {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Write(document, writer, fullDocument);
        return writer.ToString();
    }

    public static void Write(HighlightedDocument document, TextWriter writer, bool fullDocument = false) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);
        document = DocumentPresentation.Prepare(document);
        if (fullDocument)
            writer.Write("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>TextMate</title></head><body>");
        TextStyle defaults = SyntaxHighlighter.GetDefaultStyle(document.Theme, document.Catalog);
        writer.Write("<pre class=\"textmate\" style=\"white-space:pre;tab-size:4;font-family:monospace;");
        Color(writer, "color:#", defaults.Foreground);
        Color(writer, "background-color:#", defaults.Background);
        // A first literal newline immediately after <pre> is discarded by HTML parsers.
        writer.Write("\"><span></span>");
        ReadOnlySpan<StyledRange> ranges = document.Ranges.Span;
        int lineIndex = 0;
        foreach (HighlightedLine line in document.Lines.Span) {
            if (document.ShowLineNumbers) {
                LineNumberGutter.Write(writer, lineIndex + 1, document.LineCount);
            }
            int cursor = line.Start;
            foreach (StyledRange range in ranges.Slice(line.FirstRange, line.RangeCount)) {
                Escape(writer, document.Text.AsSpan(cursor, range.Start - cursor));
                TextStyle style = range.Style;
                if ((style.Decoration & TextDecoration.Invert) != 0) {
                    style = style with {
                        Foreground = style.Background < 0 ? defaults.Background : style.Background,
                        Background = style.Foreground < 0 ? defaults.Foreground : style.Foreground
                    };
                }

                bool link = IsSafeLink(style.Hyperlink);
                if (link) {
                    writer.Write("<a style=\"color:inherit;text-decoration:none\" href=\"");
                    Escape(writer, style.Hyperlink.AsSpan());
                    writer.Write("\">");
                }
                writer.Write("<span style=\"");
                Color(writer, "color:#", style.Foreground);
                Color(writer, "background-color:#", style.Background);
                if ((style.Decoration & TextDecoration.Bold) != 0)
                    writer.Write("font-weight:bold;");
                if ((style.Decoration & TextDecoration.Italic) != 0)
                    writer.Write("font-style:italic;");
                if ((style.Decoration & TextDecoration.Dim) != 0)
                    writer.Write("opacity:0.5;");
                if ((style.Decoration & TextDecoration.Conceal) != 0)
                    writer.Write("visibility:hidden;");
                if ((style.Decoration & (TextDecoration.Underline | TextDecoration.Strikethrough)) != 0) {
                    writer.Write("text-decoration:");
                    if ((style.Decoration & TextDecoration.Underline) != 0)
                        writer.Write(" underline");
                    if ((style.Decoration & TextDecoration.Strikethrough) != 0)
                        writer.Write(" line-through");
                    writer.Write(';');
                }
                writer.Write("\">");
                Escape(writer, document.Text.AsSpan(range.Start, range.Length));
                writer.Write("</span>");
                if (link)
                    writer.Write("</a>");
                cursor = range.Start + range.Length;
            }
            int next = ++lineIndex < document.LineCount ? document.Lines.Span[lineIndex].Start : document.Text.Length;
            Escape(writer, document.Text.AsSpan(cursor, next - cursor));
        }
        writer.Write("</pre>");
        if (fullDocument)
            writer.Write("</body></html>");
    }

    private static bool IsSafeLink(string? link) {
        if (string.IsNullOrWhiteSpace(link))
            return false;
        foreach (char value in link) {
            if (char.IsControl(value))
                return false;
        }
        return Uri.TryCreate(link, UriKind.RelativeOrAbsolute, out Uri? uri) && (!uri.IsAbsoluteUri || uri.Scheme is "http" or "https" or "mailto" or "file");
    }

    private static void Color(TextWriter writer, string property, int rgb) {
        if (rgb < 0)
            return;
        Span<char> hex = stackalloc char[6];
        (rgb & 0xffffff).TryFormat(hex, out int written, "X6", CultureInfo.InvariantCulture);
        writer.Write(property);
        writer.Write(hex[..written]);
        writer.Write(';');
    }

    private static void Escape(TextWriter writer, ReadOnlySpan<char> text) {
        int index;
        while ((index = text.IndexOfAny(Escapes)) >= 0) {
            writer.Write(text[..index]);
            writer.Write(text[index] switch {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => "&#39;"
            });
            text = text[(index + 1)..];
        }
        writer.Write(text);
    }
}
