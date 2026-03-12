namespace PSTextMate.Core;

/// <summary>
/// Represents syntax-highlighted text ready for rendering.
/// Provides a clean, consistent output type.
/// Implements IRenderable so it can be used directly with Spectre.Console.
/// </summary>
public sealed class HighlightedText : IRenderable {
    private static readonly IRenderable[] s_emptyRenderables = [];

    private IRenderable[] _renderables = s_emptyRenderables;

    public HighlightedText() {
    }

    public HighlightedText(
        IRenderable[] renderables,
        bool showLineNumbers = false,
        string language = "",
        bool page = false,
        IReadOnlyList<string>? sourceLines = null
    ) {
        Renderables = renderables;
        ShowLineNumbers = showLineNumbers;
        Language = language ?? string.Empty;
        Page = page;
        SourceLines = sourceLines;
    }

    /// <summary>
    /// The highlighted renderables ready for display.
    /// </summary>
    public IRenderable[] Renderables {
        get => _renderables;
        set => _renderables = value ?? s_emptyRenderables;
    }

    // Optional view into an external renderable sequence to avoid allocating
    // new arrays when rendering paged slices. When _viewSource is non-null,
    // rendering methods use the view (Skip/Take) rather than the `Renderables` array.
    private IEnumerable<IRenderable>? _viewSource;
    private IReadOnlyList<IRenderable>? _viewSourceList;
    private int _viewStart;
    private int _viewCount;
    // When a view is active, keep the total document line count when available
    // so line-number gutter width can be computed against the full document
    // (prevents gutter from changing across pages).
    private int _documentLineCount = -1;

    // Optional source text retained for pager search fast path.
    // This is intentionally opt-in to avoid unnecessary memory usage when paging is not used.
    internal IReadOnlyList<string>? SourceLines { get; private set; }

    /// <summary>
    /// When true, prepend line numbers with a gutter separator.
    /// </summary>
    public bool ShowLineNumbers { get; set; }

    /// <summary>
    /// Starting line number for the gutter.
    /// </summary>
    public int LineNumberStart { get; set; } = 1;

    /// <summary>
    /// Optional fixed width for the line number column.
    /// </summary>
    public int? LineNumberWidth { get; set; }

    /// <summary>
    /// Separator inserted between the line number and content.
    /// </summary>
    public string GutterSeparator { get; set; } = " │ ";

    /// <summary>
    /// Number of lines contained in this highlighted text.
    /// </summary>
    public int LineCount => _viewSource is null ? Renderables.Length : _viewCount;

    /// <summary>
    /// Configure this instance to render a view (slice) of an external renderable
    /// sequence without allocating a new array. Call <see cref="ClearView"/> to
    /// return to rendering the local <see cref="Renderables"/> array.
    /// </summary>
    public void SetView(IEnumerable<IRenderable> source, int start, int count) {
        _viewSource = source ?? throw new ArgumentNullException(nameof(source));
        _viewSourceList = source as IReadOnlyList<IRenderable>;
        _viewStart = Math.Max(0, start);
        _viewCount = Math.Max(0, count);
        // Try to capture the full source count when possible (ICollection/IReadOnlyCollection/IList)
        _documentLineCount = _viewSourceList is not null
            ? _viewSourceList.Count
            : source is ICollection<IRenderable> coll
            ? coll.Count
            : source is IReadOnlyCollection<IRenderable> rocoll
                ? rocoll.Count
                : source is ICollection nonGeneric ? nonGeneric.Count : -1;
    }

    /// <summary>
    /// Clears any active view so the instance renders its own <see cref="Renderables"/> array.
    /// </summary>
    public void ClearView() {
        _viewSource = null;
        _viewSourceList = null;
        _viewStart = 0;
        _viewCount = 0;
        _documentLineCount = -1;
    }

    private IEnumerable<IRenderable> GetRenderablesEnumerable() {
        return _viewSourceList is not null
            ? EnumerateViewList(_viewSourceList, _viewStart, _viewCount)
            : _viewSource is null ? Renderables : _viewSource.Skip(_viewStart).Take(_viewCount);
    }

    private static IEnumerable<IRenderable> EnumerateViewList(IReadOnlyList<IRenderable> source, int start, int count) {
        int begin = Math.Clamp(start, 0, source.Count);
        int end = Math.Clamp(begin + Math.Max(0, count), begin, source.Count);
        for (int i = begin; i < end; i++) {
            yield return source[i];
        }
    }

    internal void SetSourceLines(IReadOnlyList<string>? sourceLines)
        => SourceLines = sourceLines;
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// When true, writing this renderable should use the interactive pager.
    /// </summary>
    public bool Page { get; set; }

    /// <summary>
    /// Renders the highlighted text by combining all renderables into a single output.
    /// </summary>
    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
        // Delegate to Rows which efficiently renders all renderables
        var rows = new Rows(GetRenderablesEnumerable());
        return !ShowLineNumbers ? ((IRenderable)rows).Render(options, maxWidth) : RenderWithLineNumbers(rows, options, maxWidth);
    }

    /// <summary>
    /// Measures the dimensions of the highlighted text.
    /// </summary>
    public Measurement Measure(RenderOptions options, int maxWidth) {
        // Delegate to Rows for measurement
        var rows = new Rows(GetRenderablesEnumerable());
        return !ShowLineNumbers ? ((IRenderable)rows).Measure(options, maxWidth) : MeasureWithLineNumbers(rows, options, maxWidth);
    }

    // Inner wrapper that presents the HighlightedText's content (with or without line numbers)
    // as an IRenderable so it can be embedded in containers like Panel without recursion.
    private sealed class InnerContentRenderable : IRenderable {
        private readonly HighlightedText _parent;
        public InnerContentRenderable(HighlightedText parent) {
            _parent = parent;
        }

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
            var rows = new Rows(_parent.GetRenderablesEnumerable());
            return !_parent.ShowLineNumbers
                ? ((IRenderable)rows).Render(options, maxWidth)
                : _parent.RenderWithLineNumbers(rows, options, maxWidth);
        }

        public Measurement Measure(RenderOptions options, int maxWidth) {
            var rows = new Rows(_parent.GetRenderablesEnumerable());
            return !_parent.ShowLineNumbers
                ? ((IRenderable)rows).Measure(options, maxWidth)
                : _parent.MeasureWithLineNumbers(rows, options, maxWidth);
        }
    }

    private IEnumerable<Segment> RenderWithLineNumbers(Rows rows, RenderOptions options, int maxWidth) {
        (List<Segment> segments, int width, int contentWidth) = RenderInnerSegments(rows, options, maxWidth);
        return PrefixLineNumbers(segments, options, width, contentWidth);
    }

    private Measurement MeasureWithLineNumbers(Rows rows, RenderOptions options, int maxWidth) {
        (List<Segment> segments, int width, int contentWidth) = RenderInnerSegments(rows, options, maxWidth);
        Measurement measurement = ((IRenderable)rows).Measure(options, contentWidth);
        int gutterWidth = width + GutterSeparator.Length;
        return new Measurement(measurement.Min + gutterWidth, measurement.Max + gutterWidth);
    }

    private (List<Segment> segments, int width, int contentWidth) RenderInnerSegments(Rows rows, RenderOptions options, int maxWidth) {
        int width = ResolveLineNumberWidth(LineCount);
        int contentWidth = Math.Max(1, maxWidth - (width + GutterSeparator.Length));
        var segments = ((IRenderable)rows).Render(options, contentWidth).ToList();

        int actualLineCount = CountLines(segments);
        int actualWidth = ResolveLineNumberWidth(actualLineCount);
        // If we have a document-wide line count available or an explicit
        // LineNumberWidth, prefer that value and avoid reflowing based on
        // measured content, which would make the gutter change size.
        if (!LineNumberWidth.HasValue && _documentLineCount <= 0 && actualWidth != width) {
            width = actualWidth;
            contentWidth = Math.Max(1, maxWidth - (width + GutterSeparator.Length));
            segments = [.. ((IRenderable)rows).Render(options, contentWidth)];
        }

        return (segments, width, contentWidth);
    }

    private IEnumerable<Segment> PrefixLineNumbers(List<Segment> segments, RenderOptions options, int width, int contentWidth) {
        int lineNumber = LineNumberStart;

        foreach (List<Segment> line in SplitLines(segments)) {
            string label = lineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(width) + GutterSeparator;
            int gutterWidth = width + GutterSeparator.Length;
            foreach (Segment segment in ((IRenderable)new Text(label)).Render(options, gutterWidth)) {
                yield return segment;
            }

            foreach (Segment segment in line) {
                yield return segment;
            }

            yield return Segment.LineBreak;
            lineNumber++;
        }
    }

    private static IEnumerable<List<Segment>> SplitLines(IEnumerable<Segment> segments) {
        List<Segment> current = [];
        bool sawLineBreak = false;

        foreach (Segment segment in segments) {
            if (segment.IsLineBreak) {
                yield return current;
                current = [];
                sawLineBreak = true;
                continue;
            }

            current.Add(segment);
        }

        if (current.Count > 0 || !sawLineBreak) {
            if (current.Count > 0) {
                yield return current;
            }
        }
    }

    private static int CountLines(List<Segment> segments) {
        if (segments.Count == 0) {
            return 0;
        }

        int lineBreaks = segments.Count(segment => segment.IsLineBreak);
        return lineBreaks == 0 ? 1 : segments[^1].IsLineBreak ? lineBreaks : lineBreaks + 1;
    }


    private int ResolveLineNumberWidth(int lineCount) {
        if (LineNumberWidth.HasValue && LineNumberWidth.Value > 0) {
            return LineNumberWidth.Value;
        }

        // Prefer computing width based on the total document line count when
        // available so the gutter remains stable across paged views.
        int effectiveTotal = _documentLineCount > 0 ? _documentLineCount : lineCount;
        int lastLineNumber = LineNumberStart + Math.Max(0, effectiveTotal - 1);
        return lastLineNumber.ToString(CultureInfo.InvariantCulture).Length;
    }

    /// <summary>
    /// Wraps the highlighted text in a Spectre.Console Panel.
    /// </summary>
    /// <param name="title">Optional panel title</param>
    /// <param name="border">Border style to use (default: Rounded)</param>
    /// <returns>Panel containing the highlighted text</returns>
    public Panel ToPanel(string? title = null, BoxBorder? border = null) {
        // Build the panel around the actual inner content instead of `this` to avoid
        // creating nested panels when consumers already wrap the object.
        IRenderable content = !ShowLineNumbers
            ? new Rows(GetRenderablesEnumerable())
            : new InnerContentRenderable(this);

        var panel = new Panel(content);
        panel.Padding(0, 0);
        panel.Expand();

        if (!string.IsNullOrEmpty(title)) {
            panel.Header(title);
        }

        if (border != null) {
            panel.Border(border);
        }
        else {
            panel.Border(BoxBorder.Rounded);
        }

        return panel;
    }

    /// <summary>
    /// Wraps the highlighted text with padding.
    /// </summary>
    /// <param name="padding">Padding to apply</param>
    /// <returns>Padder containing the highlighted text</returns>
    public Padder WithPadding(Padding padding) => new(this, padding);

    /// <summary>
    /// Wraps the highlighted text with uniform padding on all sides.
    /// </summary>
    /// <param name="size">Padding size for all sides</param>
    /// <returns>Padder containing the highlighted text</returns>
    public Padder WithPadding(int size) => new(this, new Padding(size));
    public void ShowPager() {
        if (LineCount <= 0) return;

        var pager = new Pager(this);
        pager.Show();
    }

    /// <summary>
    /// Renders this highlighted text to a string.
    /// </summary>
    public string? Write()
        => Writer.Write(this, Page);

    public override string ToString()
        => Writer.WriteToString(this);
}
