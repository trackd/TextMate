namespace PSTextMate.Terminal;

internal static class PagerHighlighting {
    internal static bool TryBuildStructuredHighlightRenderable(
        IRenderable renderable,
        string query,
        string rowStyle,
        string matchStyle,
        out IRenderable highlighted
    ) {
        highlighted = renderable;
        if (string.IsNullOrEmpty(query) || renderable is not Table table) {
            return false;
        }

        highlighted = CloneTableWithHighlight(table, query, rowStyle, matchStyle);
        return true;
    }

    internal static string BuildHighlightedMarkup(string plainText, IReadOnlyList<PagerSearchHit> hits, string matchStyle) {
        if (plainText.Length == 0) {
            return string.Empty;
        }

        int position = 0;
        var builder = new StringBuilder(plainText.Length + Math.Max(32, hits.Count * 24));
        foreach (PagerSearchHit hit in hits.OrderBy(static h => h.Offset)) {
            int start = Math.Clamp(hit.Offset, 0, plainText.Length);
            int length = Math.Clamp(hit.Length, 0, plainText.Length - start);
            if (length <= 0 || start < position) {
                continue;
            }

            if (start > position) {
                builder.Append(Markup.Escape(plainText[position..start]));
            }

            string matchPart = plainText.Substring(start, length);
            builder.Append('[')
                .Append(matchStyle)
                .Append(']')
                .Append(Markup.Escape(matchPart))
                .Append("[/]");

            position = start + length;
        }

        if (position < plainText.Length) {
            builder.Append(Markup.Escape(plainText[position..]));
        }

        return builder.ToString();
    }

    internal static string NormalizeText(string? text) {
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .TrimEnd('\n');
    }

    private static Table CloneTableWithHighlight(Table source, string query, string rowStyle, string matchStyle) {
        var clone = new Table {
            Border = source.Border,
            BorderStyle = source.BorderStyle,
            UseSafeBorder = source.UseSafeBorder,
            ShowHeaders = source.ShowHeaders,
            ShowRowSeparators = source.ShowRowSeparators,
            ShowFooters = source.ShowFooters,
            Expand = source.Expand,
            Width = source.Width,
            Title = source.Title is null ? null : new TableTitle(source.Title.Text, source.Title.Style),
            Caption = source.Caption is null ? null : new TableTitle(source.Caption.Text, source.Caption.Style)
        };

        foreach (TableColumn sourceColumn in source.Columns) {
            IRenderable header = HighlightRenderableNode(sourceColumn.Header, query, applyRowStyle: false, rowStyle, matchStyle);
            IRenderable? footer = sourceColumn.Footer is null
                ? null
                : HighlightRenderableNode(sourceColumn.Footer, query, applyRowStyle: false, rowStyle, matchStyle);
            var column = new TableColumn(header) {
                Width = sourceColumn.Width,
                Padding = sourceColumn.Padding,
                NoWrap = sourceColumn.NoWrap,
                Alignment = sourceColumn.Alignment,
                Footer = footer
            };

            clone.AddColumn(column);
        }

        foreach (TableRow sourceRow in source.Rows) {
            bool rowHasMatch = sourceRow.Any(cell => RenderableContainsQuery(cell, query));
            var rowItems = new List<IRenderable>();
            foreach (IRenderable sourceCell in sourceRow) {
                rowItems.Add(HighlightRenderableNode(sourceCell, query, rowHasMatch, rowStyle, matchStyle));
            }

            clone.AddRow(rowItems);
        }

        return clone;
    }

    private static bool RenderableContainsQuery(IRenderable renderable, string query) {
        if (string.IsNullOrEmpty(query)) {
            return false;
        }

        string plainText = NormalizeText(ExtractRenderableText(renderable));
        return plainText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static IRenderable HighlightRenderableNode(
        IRenderable renderable,
        string query,
        bool applyRowStyle,
        string rowStyle,
        string matchStyle
    ) {
        return renderable switch {
            Text or Markup or Paragraph => HighlightLeafRenderable(renderable, query, applyRowStyle, rowStyle, matchStyle),
            _ => renderable,
        };
    }

    private static IRenderable HighlightLeafRenderable(
        IRenderable renderable,
        string query,
        bool applyRowStyle,
        string rowStyle,
        string matchStyle
    ) {
        string plainText = NormalizeText(ExtractRenderableText(renderable));
        if (plainText.Length == 0) {
            return renderable;
        }

        string highlighted = BuildHighlightedMarkupFromQuery(plainText, query, applyRowStyle, rowStyle, matchStyle);
        string baseline = applyRowStyle
            ? $"[{rowStyle}]{Markup.Escape(plainText)}[/]"
            : Markup.Escape(plainText);

        return string.Equals(highlighted, baseline, StringComparison.Ordinal)
            ? renderable
            : new Markup(highlighted);
    }

    private static string ExtractRenderableText(IRenderable renderable) {
        if (renderable is Text text) {
            return text.ToString() ?? string.Empty;
        }

        try {
            string rendered = Writer.WriteToString(renderable, width: 200);
            return VTHelpers.StripAnsi(rendered);
        }
        catch {
            return string.Empty;
        }
    }

    private static string BuildHighlightedMarkupFromQuery(
        string plainText,
        string query,
        bool applyRowStyle,
        string rowStyle,
        string matchStyle
    ) {
        if (string.IsNullOrEmpty(plainText) || string.IsNullOrEmpty(query)) {
            string escaped = Markup.Escape(plainText);
            return applyRowStyle ? $"[{rowStyle}]{escaped}[/]" : escaped;
        }

        static void AppendNormalText(StringBuilder builder, string text, bool applyRowStyle, string rowStyle) {
            if (text.Length == 0) {
                return;
            }

            string escaped = Markup.Escape(text);
            if (applyRowStyle) {
                builder.Append('[')
                    .Append(rowStyle)
                    .Append(']')
                    .Append(escaped)
                    .Append("[/]");
            }
            else {
                builder.Append(escaped);
            }
        }

        int position = 0;
        int queryLength = query.Length;
        var builder = new StringBuilder(plainText.Length + 32);
        while (position < plainText.Length) {
            int hitOffset = plainText.IndexOf(query, position, StringComparison.OrdinalIgnoreCase);
            if (hitOffset < 0) {
                AppendNormalText(builder, plainText[position..], applyRowStyle, rowStyle);
                break;
            }

            if (hitOffset > position) {
                AppendNormalText(builder, plainText[position..hitOffset], applyRowStyle, rowStyle);
            }

            int hitLength = Math.Min(queryLength, plainText.Length - hitOffset);
            builder.Append('[')
                .Append(matchStyle)
                .Append(']')
                .Append(Markup.Escape(plainText.Substring(hitOffset, hitLength)))
                .Append("[/]");

            position = hitOffset + Math.Max(1, hitLength);
        }

        return builder.ToString();
    }
}
