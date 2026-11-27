namespace PSTextMate.Core;

/// <summary>A styled UTF-16 range into the document's displayed Text string.</summary>
public readonly record struct StyledRange(int Start, int Length, TextStyle Style);

/// <summary>A source line, excluding its terminator. Ranges index the document's range array.</summary>
public readonly record struct HighlightedLine(int Start, int Length, int FirstRange, int RangeCount);

/// <summary>Immutable source text and styled ranges. No rendering framework is required.</summary>
public sealed class HighlightedDocument {
    public string Source { get; }
    /// <summary>Displayed text. Ranges and lines index this string; Source retains the original input.</summary>
    public string Text { get; }
    public string Language { get; }
    public string Theme { get; }
    public bool ShowLineNumbers { get; }
    public int LineCount => Lines.Length;
    public ReadOnlyMemory<StyledRange> Ranges { get; }
    public ReadOnlyMemory<HighlightedLine> Lines { get; }
    public ReadOnlyMemory<PlacedImage> Images { get; }
    public long LimitExceededCount { get; }
    internal HighlightingSnapshot Catalog { get; }
    internal HighlightedDocument? LayoutSource { get; private set; }
    internal RendererOptions LayoutOptions { get; private set; }
    internal bool IsPresented { get; }
    internal TextContent? Content { get; private set; }

    internal HighlightedDocument(string source, string language, string theme, bool lineNumbers, StyledRange[] ranges, HighlightedLine[] lines, long limitExceededCount, string? text = null, HighlightingSnapshot? catalog = null, PlacedImage[]? images = null) {
        Catalog = catalog ?? HighlightingExtensions.Snapshot;
        Source = source;
        Text = text ?? source;
        IsPresented = text is not null;
        Language = language;
        Theme = theme;
        ShowLineNumbers = lineNumbers;
        Ranges = ranges;
        Lines = lines;
        Images = images ?? [];
        LimitExceededCount = limitExceededCount;
    }

    internal HighlightedDocument WithPresentation(string text, StyledRange[] ranges, HighlightedLine[] lines, PlacedImage[]? images = null) =>
        new(Source, Language, Theme, ShowLineNumbers, ranges, lines, LimitExceededCount, text, Catalog, images);

    private HighlightedDocument(HighlightedDocument original, string? text = null, bool presented = true, bool? lineNumbers = null) {
        LayoutSource = original.LayoutSource;
        LayoutOptions = original.LayoutOptions;
        Catalog = original.Catalog;
        Source = original.Source;
        Text = text ?? original.Text;
        Language = original.Language;
        Theme = original.Theme;
        ShowLineNumbers = lineNumbers ?? original.ShowLineNumbers;
        Ranges = original.Ranges;
        Lines = original.Lines;
        Images = original.Images;
        LimitExceededCount = original.LimitExceededCount;
        IsPresented = presented;
        Content = original.Content;
    }

    internal HighlightedDocument WithIdentityPresentation() => new(this);
    internal HighlightedDocument AsReflowable() => new(this, presented: false);
    internal HighlightedDocument WithContent(TextContent content) => new(this) { Content = content };
    internal HighlightedDocument WithoutLineNumbers() => new(this, lineNumbers: false);
    internal HighlightedDocument WithLayoutContext(HighlightedDocument source, RendererOptions options) {
        var result = new HighlightedDocument(this) {
            LayoutSource = source.LayoutSource ?? source,
            LayoutOptions = options
        };
        return result;
    }
    internal HighlightedDocument WithSafeText(string text) => new(this, text, IsPresented);

    public TextBounds GetImageBounds(int index) {
        PlacedImage image = Images.Span[index];
        return GetBounds(image.Start, image.Image.Width, image.Image.Height);
    }

    private TextBounds GetBounds(int start, int width, int height) {
        ReadOnlySpan<HighlightedLine> lines = Lines.Span;
        int low = 0;
        int high = lines.Length;
        while (low + 1 < high) {
            int middle = (low + high) / 2;
            if (lines[middle].Start <= start)
                low = middle;
            else
                high = middle;
        }
        int column = TerminalCells.Measure(Text.AsSpan(lines[low].Start, start - lines[low].Start));
        if (ShowLineNumbers)
            column += LineNumberGutter.Digits(LineCount) + 3;
        return new(column, low, width, height);
    }

    /// <summary>Writes ANSI-styled or plain text without allocating a string for each token.</summary>
    public void WriteTo(TextWriter writer, bool ansi = true) => TextRenderer.Write(this, writer, ansi);

    public string ToAnsi() => TextRenderer.FormatText(this, ansi: true);

    /// <summary>Converts this document to another output representation.</summary>
    public object ConvertTo(OutputFormat format) => OutputRenderer.Render(this, format);
    public HighlightedDocument ToDocument() => (HighlightedDocument)ConvertTo(OutputFormat.Text);
    public object ToSpectre() => ConvertTo(OutputFormat.Spectre);
    public object ToFullscreen() => ConvertTo(OutputFormat.Fullscreen);
    public TextContent AsContent() => TextContent.FromDocument(this);

    public string ToHtml(bool fullDocument = false) => HtmlRenderer.Format(this, fullDocument);

    public void WriteHtml(TextWriter writer, bool fullDocument = false) => HtmlRenderer.Write(this, writer, fullDocument);

    public override string ToString() => Source;
}
