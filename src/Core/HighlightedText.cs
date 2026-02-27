using System.Globalization;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;
using PSTextMate.Utilities;

namespace PSTextMate.Core;

/// <summary>
/// Represents syntax-highlighted text ready for rendering.
/// Provides a clean, consistent output type.
/// Implements IRenderable so it can be used directly with Spectre.Console.
/// </summary>
public sealed class HighlightedText : Renderable {
    /// <summary>
    /// The highlighted renderables ready for display.
    /// </summary>
    public IRenderable[] Renderables { get; set; } = [];

    // Optional view into an external renderable sequence to avoid allocating
    // new arrays when rendering paged slices. When _viewSource is non-null,
    // rendering methods use the view (Skip/Take) rather than the `Renderables` array.
    private IEnumerable<IRenderable>? _viewSource;
    private int _viewStart;
    private int _viewCount;

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
        _viewStart = Math.Max(0, start);
        _viewCount = Math.Max(0, count);
    }

    /// <summary>
    /// Clears any active view so the instance renders its own <see cref="Renderables"/> array.
    /// </summary>
    public void ClearView() {
        _viewSource = null;
        _viewStart = 0;
        _viewCount = 0;
    }

    private IEnumerable<IRenderable> GetRenderablesEnumerable() =>
        _viewSource is null ? Renderables : _viewSource.Skip(_viewStart).Take(_viewCount);
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// When true, consumers should render this highlighted text inside a Panel.
    /// This is preserved across slices and allows the pager to respect panel state.
    /// </summary>
    public bool WrapInPanel { get; set; }

    /// <summary>
    /// Renders the highlighted text by combining all renderables into a single output.
    /// </summary>
    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
        // If a panel wrapper is requested, render the inner content via a dedicated IRenderable
        // and let Spectre.Console's Panel handle borders/padding.
        if (WrapInPanel) {
            // Fast path: if we don't need line numbers, wrap the raw rows directly
            // to avoid creating the InnerContentRenderable wrapper.
            if (!ShowLineNumbers) {
                var rowsInner = new Rows(GetRenderablesEnumerable());
                var panelInner = new Panel(rowsInner) { Padding = new Padding(0, 0) };
                return ((IRenderable)panelInner).Render(options, maxWidth);
            }

            var inner = new InnerContentRenderable(this);
            var panel = new Panel(inner) { Padding = new Padding(0, 0) };
            return ((IRenderable)panel).Render(options, maxWidth);
        }

        // Delegate to Rows which efficiently renders all renderables
        var rows = new Rows(GetRenderablesEnumerable());
        return !ShowLineNumbers ? ((IRenderable)rows).Render(options, maxWidth) : RenderWithLineNumbers(rows, options, maxWidth);
    }

    /// <summary>
    /// Measures the dimensions of the highlighted text.
    /// </summary>
    protected override Measurement Measure(RenderOptions options, int maxWidth) {
        if (WrapInPanel) {
            if (!ShowLineNumbers) {
                var rowsInner = new Rows(GetRenderablesEnumerable());
                var panelInner = new Panel(rowsInner) { Padding = new Padding(0, 0) };
                return ((IRenderable)panelInner).Measure(options, maxWidth);
            }

            var inner = new InnerContentRenderable(this);
            var panel = new Panel(inner) { Padding = new Padding(0, 0) };
            return ((IRenderable)panel).Measure(options, maxWidth);
        }

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
        if (actualWidth != width) {
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

    // Helper used by external callers to measure this instance's renderables by
    // height (in rows) for a given width. Returns an array of heights aligned
    // with the current `Renderables` array (or the underlying source when a
    // view is active).
    public int[] MeasureRenderables(int width) {
        Capabilities caps = AnsiConsole.Console.Profile.Capabilities;
        var size = new Size(width, Math.Max(1, Console.WindowHeight));
        var options = new RenderOptions(caps, size);

        IEnumerable<IRenderable> source = _viewSource is null ? Renderables : _viewSource;
        var list = new List<int>(source.Count());

        foreach (IRenderable? r in source) {
            if (r is null) {
                list.Add(0);
                continue;
            }

            try {
                var segments = r.Render(options, width).ToList();
                int lines = CountLines(segments);
                if (lines <= 0) lines = 1;
                list.Add(lines);
            }
            catch {
                list.Add(1);
            }
        }

        return [.. list];
    }

    private int ResolveLineNumberWidth(int lineCount) {
        if (LineNumberWidth.HasValue && LineNumberWidth.Value > 0) {
            return LineNumberWidth.Value;
        }

        int lastLineNumber = LineNumberStart + Math.Max(0, lineCount - 1);
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
        IRenderable content = !ShowLineNumbers ? new Rows(Renderables) : new InnerContentRenderable(this);

        var panel = new Panel(content);
        panel.Padding(0, 0);

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
    /// <summary>
    /// Create a page-scoped HighlightedText that reuses this instance's settings
    /// but contains only a slice of the underlying renderables.
    /// </summary>
    /// <param name="start">Zero-based start index into <see cref="Renderables"/>.</param>
    /// <param name="count">Number of renderables to include.</param>
    /// <param name="overrideLineNumberWidth">Optional stable gutter width to apply to the slice.</param>
    /// <returns>A new <see cref="HighlightedText"/> representing the requested slice.</returns>
    public HighlightedText Slice(int start, int count, int? overrideLineNumberWidth = null) {
        return new HighlightedText {
            Renderables = [.. Renderables.Skip(start).Take(count)],
            ShowLineNumbers = ShowLineNumbers,
            LineNumberStart = LineNumberStart + start,
            LineNumberWidth = overrideLineNumberWidth ?? LineNumberWidth,
            GutterSeparator = GutterSeparator,
            Language = Language,
            WrapInPanel = WrapInPanel
        };
    }
    public void ShowPager() {
        if (LineCount <= 0) return;

        using var pager = new Pager(this);
        pager.Show();
    }
    public IRenderable? AutoPage() {
        if (LineCount > Console.WindowHeight - 2) {
            ShowPager();
            return null;
        }
        return this;
    }
}
