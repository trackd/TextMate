using System.Globalization;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;

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
    public required IRenderable[] Renderables { get; init; }

    /// <summary>
    /// When true, prepend line numbers with a gutter separator.
    /// </summary>
    public bool ShowLineNumbers { get; init; }

    /// <summary>
    /// Starting line number for the gutter.
    /// </summary>
    public int LineNumberStart { get; init; } = 1;

    /// <summary>
    /// Optional fixed width for the line number column.
    /// </summary>
    public int? LineNumberWidth { get; init; }

    /// <summary>
    /// Separator inserted between the line number and content.
    /// </summary>
    public string GutterSeparator { get; init; } = " │ ";

    /// <summary>
    /// Number of lines contained in this highlighted text.
    /// </summary>
    public int LineCount => Renderables.Length;

    /// <summary>
    /// Renders the highlighted text by combining all renderables into a single output.
    /// </summary>
    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
        // Delegate to Rows which efficiently renders all renderables
        var rows = new Rows(Renderables);

        return !ShowLineNumbers ? ((IRenderable)rows).Render(options, maxWidth) : RenderWithLineNumbers(rows, options, maxWidth);
    }

    /// <summary>
    /// Measures the dimensions of the highlighted text.
    /// </summary>
    protected override Measurement Measure(RenderOptions options, int maxWidth) {
        // Delegate to Rows for measurement
        var rows = new Rows(Renderables);

        return !ShowLineNumbers ? ((IRenderable)rows).Measure(options, maxWidth) : MeasureWithLineNumbers(rows, options, maxWidth);
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
            foreach (Segment segment in ((IRenderable)new Text(label)).Render(options, contentWidth)) {
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
        Panel panel = new(this);

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

    // public override string ToString() => ToPanel();

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
}
