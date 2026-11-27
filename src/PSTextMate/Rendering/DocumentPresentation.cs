namespace PSTextMate.Core;

/// <summary>Compiles Markdown into backend-independent displayed text and styles.</summary>
public interface IMarkdownPresentation {
    HighlightedDocument Render(HighlightedDocument document, RendererOptions options);
}

/// <summary>Optional image provider; output uses the same colored cells in every backend.</summary>
public interface IImagePresentation {
    TerminalGraphics GetTerminalGraphics() => default;
    HighlightedDocument? Render(HighlightedDocument document, string source, string? baseDirectory, int width, int height = 0);
}

/// <summary>Applies content decisions once, before conversion to a rendering framework.</summary>
public static class DocumentPresentation {
    internal static TerminalGraphics GetTerminalGraphics() => Images.Value?.GetTerminalGraphics() ?? default;
    private static readonly Lazy<IMarkdownPresentation> Markdown = new(static () =>
        OutputRenderer.LoadPresentation<IMarkdownPresentation>("Markdown"));
    private static readonly Lazy<IImagePresentation?> Images = new(static () =>
        OutputRenderer.HasPresentation("Images") ? OutputRenderer.LoadPresentation<IImagePresentation>("Images") : null);

    public static HighlightedDocument? Image(HighlightedDocument document, string source, RendererOptions options) =>
        Images.Value?.Render(document, source, options.BaseDirectory, options.Width > 0 ? options.Width : 80, options.Height);

    public static HighlightedDocument Prepare(HighlightedDocument document, RendererOptions options = default) {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Content is { } content)
            return content.Layout(options.Width > 0 ? options.Width : 80).WithLayoutContext(document, options);
        if (document.IsPresented)
            return document;
        int width = options.Width > 0 ? options.Width : 80;
        if (width < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Presentation width must be positive.");
        if (document.Language == "markdown" && !options.Alternate) {
            int reserved = document.ShowLineNumbers ? document.LineCount.ToString(CultureInfo.InvariantCulture).Length + 3 : 0;
            while (true) {
                HighlightedDocument result = Markdown.Value.Render(document, options with { Width = Math.Max(1, width - reserved) });
                int actual = document.ShowLineNumbers ? result.LineCount.ToString(CultureInfo.InvariantCulture).Length + 3 : 0;
                if (actual <= reserved)
                    return result.WithLayoutContext(document, options);
                reserved = actual;
            }
        }
        bool needsNormalization = document.Text.AsSpan().IndexOfAny('\t', '\r') >= 0 || TerminalText.HasControls(document.Text);
        int gutter = document.ShowLineNumbers ? document.LineCount.ToString(CultureInfo.InvariantCulture).Length + 3 : 0;
        int contentWidth = Math.Max(1, width - gutter);
        bool wrap = false;
        foreach (HighlightedLine line in document.Lines.Span) {
            if (TerminalCells.Measure(document.Text.AsSpan(line.Start, line.Length)) > contentWidth) {
                wrap = true;
                break;
            }
        }
        if (!needsNormalization && !wrap)
            return document.WithIdentityPresentation().WithLayoutContext(document, options);
        var builder = new PresentedTextBuilder();
        foreach (StyledRange range in document.Ranges.Span) {
            builder.Append(document.Text.AsSpan(builder.SourcePosition, range.Start - builder.SourcePosition), PresentedTextBuilder.Plain);
            builder.Append(document.Text.AsSpan(range.Start, range.Length), range.Style);
            builder.SourcePosition = range.Start + range.Length;
        }
        builder.Append(document.Text.AsSpan(builder.SourcePosition), PresentedTextBuilder.Plain);
        HighlightedDocument expanded = builder.Build(document);
        if (!wrap) {
            foreach (HighlightedLine line in expanded.Lines.Span) {
                if (TerminalCells.Measure(expanded.Text.AsSpan(line.Start, line.Length)) > contentWidth) {
                    wrap = true;
                    break;
                }
            }
        }
        if (!wrap)
            return expanded.WithLayoutContext(document, options);
        while (true) {
            var folded = new PresentedTextBuilder();
            for (int index = 0; index < expanded.LineCount; index++) {
                HighlightedLine line = expanded.Lines.Span[index];
                int cursor = line.Start;
                int end = cursor + line.Length;
                while (cursor < end) {
                    int count = TerminalCells.Fit(expanded.Text.AsSpan(cursor, end - cursor), contentWidth);
                    if (count == 0)
                        count = TerminalCells.NextClusterLength(expanded.Text.AsSpan(cursor));
                    if (cursor + count < end) {
                        ReadOnlySpan<char> candidate = expanded.Text.AsSpan(cursor, count);
                        int space = candidate.LastIndexOf(' ');
                        if (space > 0 && candidate[..space].IndexOfAnyExcept(' ') >= 0)
                            count = space + 1;
                    }
                    folded.AppendSlice(expanded, cursor, count);
                    cursor += count;
                    if (cursor < end)
                        folded.NewLine();
                }
                int next = index + 1 < expanded.LineCount ? expanded.Lines.Span[index + 1].Start : expanded.Text.Length;
                folded.Append(expanded.Text.AsSpan(end, next - end), PresentedTextBuilder.Plain);
            }
            HighlightedDocument result = folded.Build(document);
            int actualGutter = document.ShowLineNumbers ? result.LineCount.ToString(CultureInfo.InvariantCulture).Length + 3 : 0;
            int actualWidth = Math.Max(1, width - actualGutter);
            if (actualWidth >= contentWidth)
                return result.WithLayoutContext(document, options);
            contentWidth = actualWidth;
        }
    }
}

/// <summary>Builds one styled presentation; no per-character objects or backend types.</summary>
