namespace PSTextMate.Core;

/// <summary>A column with a fixed cell width, or zero to share the remaining width.</summary>
public readonly record struct TextColumn(TextContent Content, int Width = 0);

public sealed record PanelOptions {
    public string? Header { get; init; }
    public string? Footer { get; init; }
    public PanelBorder Border { get; init; } = PanelBorder.Rounded;
    public TextAlignment TitleAlignment { get; init; }
    public TextStyle BorderStyle { get; init; } = TextStyle.Plain;
    public TextStyle TitleStyle { get; init; } = TextStyle.Plain;
    public int HorizontalPadding { get; init; } = 1;
    public int VerticalPadding { get; init; }
}

/// <summary>Immutable, width-aware composition shared by terminal, Spectre, Fullscreen and HTML output.</summary>
public abstract class TextContent : ITextMateOutput, ITextRenderable {
    private readonly Lock _layoutGate = new();
    private HighlightedDocument? _layout;
    private int _layoutWidth;
    private protected TextContent() {

    }
    /// <summary>A default-width snapshot; use ToDocument(width) for an explicit width.</summary>
    public HighlightedDocument Document => ToDocument();
    public HighlightedDocument Layout(int width) {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        lock (_layoutGate) {
            if (_layout is null || _layoutWidth != width) {
                _layout = LayoutCore(width);
                _layoutWidth = width;
            }
            return _layout;
        }
    }
    protected abstract HighlightedDocument LayoutCore(int width);

    public static TextContent Text(string text) => Text(text, TextStyle.Plain);
    public static TextContent Text(string text, TextStyle style) {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new PresentedTextBuilder();
        builder.Append(text, style);
        return FromDocument(builder.Build(SyntaxHighlighter.PlainText(text)).AsReflowable());
    }
    public static TextContent FromAnsi(string text) => FromDocument(VT.Parse(text));
    public static TextContent Rule(string? title = null, TextStyle? style = null, TextAlignment alignment = TextAlignment.Center) =>
        new RuleContent(title ?? string.Empty, style ?? TextStyle.Plain, alignment);
    public static TextContent Progress(long completed, long total, TextStyle? style = null) =>
        new ProgressContent(completed, total, style ?? new TextStyle(TextColor.Lime));
    public static TextContent FromDocument(HighlightedDocument document) {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentContent(document);
    }

    /// <summary>Composes a custom width-aware source using the shared output adapters.</summary>
    public static TextContent FromRenderable(ITextRenderable renderable) {
        ArgumentNullException.ThrowIfNull(renderable);
        return renderable as TextContent ?? new RenderableContent(renderable);
    }

    private sealed class RenderableContent(ITextRenderable renderable) : TextContent {
        protected override HighlightedDocument LayoutCore(int width) => renderable.Layout(width);
    }

    public static TextContent Rows(params TextContent[] children) {
        ArgumentNullException.ThrowIfNull(children);
        foreach (TextContent child in children)
            ArgumentNullException.ThrowIfNull(child);
        return new RowsContent([.. children]);
    }

    public static TextContent Columns(int gap, params TextColumn[] columns) {
        ArgumentOutOfRangeException.ThrowIfNegative(gap);
        ArgumentNullException.ThrowIfNull(columns);
        foreach (TextColumn column in columns) {
            ArgumentNullException.ThrowIfNull(column.Content);
            ArgumentOutOfRangeException.ThrowIfNegative(column.Width);
        }
        return new ColumnsContent(gap, [.. columns]);
    }

    public TextContent Panel(string? header = null, string? footer = null) => new PanelContent(this, new PanelOptions { Header = header, Footer = footer });
    public TextContent Panel(PanelOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.HorizontalPadding);
        ArgumentOutOfRangeException.ThrowIfNegative(options.VerticalPadding);
        _ = PanelFrame.Characters(options.Border);
        return new PanelContent(this, options with { });
    }

    public TextContent Pad(int horizontal, int vertical = 0) {
        ArgumentOutOfRangeException.ThrowIfNegative(horizontal);
        ArgumentOutOfRangeException.ThrowIfNegative(vertical);
        return new PaddingContent(this, horizontal, vertical);
    }
    public TextContent Align(TextAlignment alignment) {
        _ = TextGeometry.AlignmentOffset(0, 0, alignment);
        return new AlignedContent(this, alignment);
    }
    /// <summary>Overrides supplied colors/links and adds decorations across this content. Default colors inherit existing styles.</summary>
    public TextContent WithStyle(TextStyle style) => new StyledContent(this, style);
    public TextContent LineNumbers(int start = 1, TextStyle? style = null) {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, 1);
        return new NumberedContent(this, start, style ?? TextStyle.Plain);
    }

    /// <summary>Creates a document that retains composition when a native container or pager changes width.</summary>
    public HighlightedDocument ToDocument(int width = 80) {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        HighlightedDocument source = SyntaxHighlighter.PlainText(string.Empty).WithContent(this);
        return DocumentPresentation.Prepare(source, new RendererOptions(Width: width, ConstrainWidth: false));
    }
    public object ConvertTo(OutputFormat format, int width = 80) =>
        OutputRenderer.Render(ToDocument(width), format, new RendererOptions(Width: width, ConstrainWidth: false));
    public object ToSpectre(int width = 80) => ConvertTo(OutputFormat.Spectre, width);
    public object ToFullscreen(int width = 80) => ConvertTo(OutputFormat.Fullscreen, width);
    public string ToHtml(bool fullDocument = false, int width = 80) => ToDocument(width).ToHtml(fullDocument);
    public void WriteTo(TextWriter writer, int width = 80, bool ansi = true) => Layout(width).WriteTo(writer, ansi);

    private sealed class DocumentContent(HighlightedDocument document) : TextContent {
        private readonly TextMateView _view = new(document);
        protected override HighlightedDocument LayoutCore(int width) {
            HighlightedDocument result = _view.Layout(width);
            return result.ShowLineNumbers ? ContentLayout.Number(result.WithoutLineNumbers(), width, 1, TextStyle.Plain) : result;
        }
    }

    private sealed class RuleContent(string title, TextStyle style, TextAlignment alignment) : TextContent {
        protected override HighlightedDocument LayoutCore(int width) {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            string safe = PanelFrame.SafeTitle(title);
            safe = safe[..TerminalCells.Fit(safe, Math.Max(0, width - 2))];
            int cells = safe.Length == 0 ? 0 : TerminalCells.Measure(safe) + 2;
            int left = TextGeometry.AlignmentOffset(width, cells, alignment);
            var builder = new PresentedTextBuilder();
            builder.AppendRepeated('─', left, style);
            if (cells > 0) {
                builder.Append(" ", style);
                builder.Append(safe, style);
                builder.Append(" ", style);
            }
            builder.AppendRepeated('─', width - left - cells, style);
            return builder.Build(SyntaxHighlighter.PlainText(title));
        }
    }

    private sealed class ProgressContent(long completed, long total, TextStyle style) : TextContent {
        protected override HighlightedDocument LayoutCore(int width) {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            var builder = new PresentedTextBuilder();
            int filled = TextProgress.Filled(width, completed, total);
            builder.AppendRepeated('█', filled, style);
            builder.AppendRepeated(' ', width - filled, TextStyle.Plain);
            return builder.Build(SyntaxHighlighter.PlainText(string.Empty));
        }
    }

    private sealed class RowsContent(TextContent[] children) : TextContent {
        protected override HighlightedDocument LayoutCore(int width) {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            var builder = new PresentedTextBuilder();
            HighlightedDocument? metadata = null;
            for (int index = 0; index < children.Length; index++) {
                HighlightedDocument child = children[index].Layout(width);
                metadata ??= child;
                if (index > 0)
                    builder.NewLine();
                builder.Append(child);
            }
            return builder.Build(metadata ?? SyntaxHighlighter.PlainText(string.Empty));
        }
    }

    private sealed class ColumnsContent(int gap, TextColumn[] columns) : TextContent {
        protected override HighlightedDocument LayoutCore(int width) {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            if (columns.Length == 0)
                return SyntaxHighlighter.PlainText(string.Empty).WithIdentityPresentation();
            Span<int> widths = columns.Length <= 32 ? stackalloc int[columns.Length] : new int[columns.Length];
            Span<int> requested = columns.Length <= 32 ? stackalloc int[columns.Length] : new int[columns.Length];
            for (int index = 0; index < columns.Length; index++) {
                requested[index] = columns[index].Width;
            }
            int remaining = TextGeometry.DistributeColumns(width, gap, requested, widths, out int actualGap);
            var documents = new HighlightedDocument?[columns.Length];
            int height = 1;
            HighlightedDocument? metadata = null;
            for (int index = 0; index < columns.Length; index++) {
                if (widths[index] == 0)
                    continue;
                HighlightedDocument child = columns[index].Content.Layout(widths[index]);
                documents[index] = child;
                metadata ??= child;
                height = Math.Max(height, child.LineCount);
            }
            var builder = new PresentedTextBuilder();
            for (int row = 0; row < height; row++) {
                if (row > 0)
                    builder.NewLine();
                for (int index = 0; index < documents.Length; index++) {
                    if (index > 0)
                        builder.AppendRepeated(' ', actualGap, TextStyle.Plain);
                    ContentLayout.Row(builder, documents[index], row, widths[index]);
                }
                builder.AppendRepeated(' ', remaining, TextStyle.Plain);
            }
            return builder.Build(metadata ?? SyntaxHighlighter.PlainText(string.Empty));
        }
    }

    private sealed class AlignedContent(TextContent child, TextAlignment alignment) : TextContent {
        protected override HighlightedDocument LayoutCore(int width) {
            HighlightedDocument document = child.Layout(width);
            var builder = new PresentedTextBuilder();
            for (int row = 0; row < document.LineCount; row++) {
                if (row > 0)
                    builder.NewLine();
                ContentLayout.Row(builder, document, row, width, alignment);
            }
            return builder.Build(document);
        }
    }

    private sealed class StyledContent(TextContent child, TextStyle style) : TextContent {
        protected override HighlightedDocument LayoutCore(int width) {
            HighlightedDocument document = child.Layout(width);
            var builder = new PresentedTextBuilder();
            int cursor = 0;
            foreach (StyledRange range in document.Ranges.Span) {
                builder.Append(document.Text.AsSpan(cursor, range.Start - cursor), Merge(TextStyle.Plain));
                builder.Append(document.Text.AsSpan(range.Start, range.Length), Merge(range.Style));
                cursor = range.Start + range.Length;
            }
            builder.Append(document.Text.AsSpan(cursor), Merge(TextStyle.Plain));
            return builder.Build(document);
        }

        private TextStyle Merge(TextStyle original) => new(style.Foreground < 0 ? original.Foreground : style.Foreground, style.Background < 0 ? original.Background : style.Background, original.Decoration | style.Decoration, style.Hyperlink ?? original.Hyperlink);
    }

    private sealed class PaddingContent(TextContent child, int horizontal, int vertical) : TextContent {
        protected override HighlightedDocument LayoutCore(int width) {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            int padding = Math.Min(horizontal, width / 2);
            int inner = width - (padding * 2);
            HighlightedDocument document = child.Layout(Math.Max(1, inner));
            var builder = new PresentedTextBuilder();
            int height = checked(document.LineCount + (vertical * 2));
            for (int row = 0; row < height; row++) {
                if (row > 0)
                    builder.NewLine();
                builder.AppendRepeated(' ', padding, TextStyle.Plain);
                ContentLayout.Row(builder, document, row - vertical, inner);
                builder.AppendRepeated(' ', padding, TextStyle.Plain);
            }
            return builder.Build(document);
        }
    }

    private sealed class NumberedContent(TextContent child, int start, TextStyle style) : TextContent {
        protected override HighlightedDocument LayoutCore(int width) {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            int reserved = LineNumberGutter.Width(start);
            while (true) {
                HighlightedDocument document = child.Layout(Math.Max(1, width - reserved));
                int actual = LineNumberGutter.Width(checked(start + document.LineCount - 1));
                if (actual <= reserved)
                    return ContentLayout.Number(document, width, start, style);
                reserved = actual;
            }
        }
    }

    private sealed class PanelContent(TextContent child, PanelOptions options) : TextContent {
        protected override HighlightedDocument LayoutCore(int width) {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            if (options.Border == PanelBorder.None)
                return child.Pad(options.HorizontalPadding, options.VerticalPadding).Layout(width);
            int inner = Math.Max(0, width - 2);
            int padding = Math.Min(options.HorizontalPadding, inner / 2);
            int contentWidth = inner - (padding * 2);
            HighlightedDocument document = child.Layout(Math.Max(1, contentWidth));
            var builder = new PresentedTextBuilder();
            ContentLayout.Border(builder, width, options.Header, false, options);
            int height = checked(document.LineCount + (options.VerticalPadding * 2));
            string characters = PanelFrame.Characters(options.Border);
            for (int row = 0; row < height; row++) {
                builder.NewLine();
                builder.Append(characters.AsSpan(5, 1), options.BorderStyle);
                builder.AppendRepeated(' ', padding, TextStyle.Plain);
                ContentLayout.Row(builder, document, row - options.VerticalPadding, contentWidth);
                builder.AppendRepeated(' ', padding, TextStyle.Plain);
                if (width > 1)
                    builder.Append(characters.AsSpan(5, 1), options.BorderStyle);
            }
            builder.NewLine();
            ContentLayout.Border(builder, width, options.Footer, true, options);
            return builder.Build(document);
        }
    }
}

internal static class ContentLayout {
    internal static void Row(PresentedTextBuilder builder, HighlightedDocument? document, int row, int width, TextAlignment alignment = TextAlignment.Left) {
        int length = 0;
        HighlightedLine line = default;
        if (width > 0 && document is not null && (uint)row < (uint)document.LineCount) {
            line = document.Lines.Span[row];
            length = TerminalCells.Fit(document.Text.AsSpan(line.Start, line.Length), width);
        }
        int cells = length > 0 ? TerminalCells.Measure(document!.Text.AsSpan(line.Start, length)) : 0;
        int left = TextGeometry.AlignmentOffset(width, cells, alignment);
        builder.AppendRepeated(' ', left, TextStyle.Plain);
        if (length > 0)
            builder.AppendSlice(document!, line.Start, length);
        builder.AppendRepeated(' ', width - cells - left, TextStyle.Plain);
    }

    internal static HighlightedDocument Number(HighlightedDocument document, int width, int start, TextStyle style) {
        var builder = new PresentedTextBuilder();
        int total = checked(start + document.LineCount - 1);
        for (int row = 0; row < document.LineCount; row++) {
            if (row > 0)
                builder.NewLine();
            string gutter = LineNumberGutter.Format(start + row, total);
            int fit = TerminalCells.Fit(gutter, width);
            builder.Append(gutter.AsSpan(0, fit), style);
            Row(builder, document, row, Math.Max(0, width - fit));
        }
        return builder.Build(document.WithoutLineNumbers());
    }

    internal static void Border(PresentedTextBuilder builder, int width, string? title, bool bottom, PanelOptions options) {
        title = PanelFrame.SafeTitle(title ?? string.Empty);
        string border = PanelFrame.Border(width, title, bottom, options.Border, options.TitleAlignment);
        if (string.IsNullOrEmpty(title)) {
            builder.Append(border, options.BorderStyle);
            return;
        }
        // The geometry helper also sanitizes and clips the title. Match its occupied slice in cells.
        int inner = Math.Max(0, width - 2);
        int titleCells = TerminalCells.Measure(title.AsSpan(0, TerminalCells.Fit(title, Math.Max(0, inner - 1))));
        int offset = 1 + TextGeometry.AlignmentOffset(inner, titleCells + (inner > 0 ? 1 : 0), options.TitleAlignment) + (inner > 0 ? 1 : 0);
        int prefix = TerminalCells.Fit(border, Math.Min(width, offset));
        int length = TerminalCells.Fit(border.AsSpan(prefix), titleCells);
        builder.Append(border.AsSpan(0, prefix), options.BorderStyle);
        builder.Append(border.AsSpan(prefix, length), options.TitleStyle);
        builder.Append(border.AsSpan(prefix + length), options.BorderStyle);
    }
}
