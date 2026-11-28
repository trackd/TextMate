using System.Globalization;
using PSTextMate.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using NeutralStyle = PSTextMate.Core.TextStyle;

namespace PSTextMate.Spectre;

/// <summary>Converts shared presentations to a native, composable Spectre renderable.</summary>
public sealed class Renderer : IOutputRenderer {
    public object Render(HighlightedDocument document, RendererOptions options) => new TextMateRenderable(document, options);
}

/// <summary>Native Spectre output which uses TextMate's shared layout at the container's width.</summary>
public sealed class TextMateRenderable : IRenderable, ITextMateOutput {
    private readonly TextMateView _view;
    private readonly Dictionary<NeutralStyle, Style> _styles = [];
    private readonly Lock _gate = new();
    private HighlightedDocument? _measured;
    private int _measuredWidth;
    private HighlightedDocument? _rendered;
    private int _renderedWidth;
    private Segment[] _segments = [];
    public HighlightedDocument Document { get; private set; }
    public string Language => Document.Language;
    public int LineCount => Document.LineCount;
    public bool ShowLineNumbers => Document.ShowLineNumbers;

    internal TextMateRenderable(HighlightedDocument document, RendererOptions options) {
        _view = new TextMateView(document, options);
        Document = _view.Layout(options.Width > 0 ? options.Width : 80);
    }

    // Members retained from the 0.x HighlightedText renderable so existing scripts keep working.

    /// <summary>Wraps the highlighted content in an expanded, unpadded panel.</summary>
    public Panel ToPanel(string? title = null, BoxBorder? border = null) {
        var panel = new Panel(this).Padding(0, 0).Expand();
        if (!string.IsNullOrEmpty(title))
            panel.Header(title);
        if (border is not null)
            panel.Border(border);
        return panel;
    }

    public Padder WithPadding(Padding padding) => new(this, padding);
    public Padder WithPadding(int size) => new(this, new Padding(size));

    /// <summary>Shows the content in the built-in pager.</summary>
    public void ShowPager() {
        if (LineCount > 0)
            TextPager.Show(Document);
    }

    /// <summary>The styled text as PowerShell would display it.</summary>
    public override string ToString() => OutputRenderer.Format(this);

    public Measurement Measure(RenderOptions options, int maxWidth) {
        HighlightedDocument document = _view.Layout(Math.Max(1, maxWidth));
        lock (_gate) {
            Document = document;
            if (!ReferenceEquals(document, _measured)) {
                int gutter = document.ShowLineNumbers ? document.LineNumberTotal.ToString(CultureInfo.InvariantCulture).Length + 3 : 0;
                _measuredWidth = 0;
                foreach (HighlightedLine line in document.Lines.Span)
                    _measuredWidth = Math.Max(_measuredWidth, TerminalCells.Measure(document.Text.AsSpan(line.Start, line.Length)) + gutter);
                _measured = document;
            }
            return new Measurement(Math.Min(1, maxWidth), Math.Min(_measuredWidth, Math.Max(1, maxWidth)));
        }
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
        HighlightedDocument document = _view.Layout(Math.Max(1, maxWidth));
        lock (_gate) {
            Document = document;
            if (!ReferenceEquals(document, _rendered) || maxWidth != _renderedWidth) {
                _segments = [.. BuildSegments(document, maxWidth)];
                _rendered = document;
                _renderedWidth = maxWidth;
            }
            return _segments;
        }
    }

    private IEnumerable<Segment> BuildSegments(HighlightedDocument document, int maxWidth) {
        for (int index = 0; index < document.LineCount; index++) {
            HighlightedLine line = document.Lines.Span[index];
            int available = Math.Max(0, maxWidth);
            if (document.ShowLineNumbers) {
                string gutter = LineNumberGutter.Format(document.GetLineNumber(index), document.LineNumberTotal);
                int fit = TerminalCells.Fit(gutter, available);
                yield return new Segment(gutter[..fit]);
                available -= TerminalCells.Measure(gutter.AsSpan(0, fit));
            }
            int end = line.Start + TerminalCells.Fit(document.Text.AsSpan(line.Start, line.Length), available);
            int cursor = line.Start;
            for (int offset = line.FirstRange; offset < line.FirstRange + line.RangeCount && cursor < end; offset++) {
                StyledRange range = document.Ranges.Span[offset];
                if (range.Start > cursor)
                    yield return new Segment(document.Text[cursor..Math.Min(end, range.Start)]);
                if (range.Start >= end)
                    break;
                int length = Math.Min(range.Length, end - range.Start);
                yield return new Segment(document.Text.Substring(range.Start, length), GetStyle(range.Style));
                cursor = range.Start + length;
            }
            if (cursor < end)
                yield return new Segment(document.Text[cursor..end]);
            if (index + 1 < document.LineCount)
                yield return Segment.LineBreak;
        }
    }

    private Style GetStyle(NeutralStyle style) {
        lock (_styles) {
            if (_styles.TryGetValue(style, out Style? existing))
                return existing;
            Decoration decoration = Decoration.None;
            if ((style.Decoration & TextDecoration.Dim) != 0)
                decoration |= Decoration.Dim;
            if ((style.Decoration & TextDecoration.SlowBlink) != 0)
                decoration |= Decoration.SlowBlink;
            if ((style.Decoration & TextDecoration.RapidBlink) != 0)
                decoration |= Decoration.RapidBlink;
            if ((style.Decoration & TextDecoration.Invert) != 0)
                decoration |= Decoration.Invert;
            if ((style.Decoration & TextDecoration.Conceal) != 0)
                decoration |= Decoration.Conceal;
            if ((style.Decoration & TextDecoration.Bold) != 0)
                decoration |= Decoration.Bold;
            if ((style.Decoration & TextDecoration.Italic) != 0)
                decoration |= Decoration.Italic;
            if ((style.Decoration & TextDecoration.Underline) != 0)
                decoration |= Decoration.Underline;
            if ((style.Decoration & TextDecoration.Strikethrough) != 0)
                decoration |= Decoration.Strikethrough;
            var result = new Style(Color(style.Foreground), Color(style.Background), decoration, style.Hyperlink);
            _styles.Add(style, result);
            return result;
        }
    }

    // Palette references stay palette colors so Spectre writes them as indexed colors.
    private static Color? Color(int value) => value < 0 ? null
        : value >= 0x1000000 ? global::Spectre.Console.Color.FromInt32(value & 0xff)
        : new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value);
}
