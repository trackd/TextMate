using PSTextMate.Core;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Text;
using TextStyle = XenoAtom.Terminal.UI.Styling.TextStyle;

namespace PSTextMate.XenoAtom;

/// <summary>Direct native XenoAtom styled text, without ANSI conversion or Spectre.</summary>
public sealed class Renderer : IOutputRenderer {
    public object Render(HighlightedDocument document, RendererOptions options) =>
        new TextMateVisual(document, options);

    internal static Paragraph BuildParagraph(HighlightedDocument document) {
        ReadOnlySpan<StyledRange> ranges = document.Ranges.Span;
        var runs = new StyledRun[ranges.Length];
        List<HyperlinkRun>? hyperlinks = null;
        string text = document.Text;
        int[]? lineShifts = null;
        if (document.ShowLineNumbers) {
            var builder = new System.Text.StringBuilder(document.Text.Length + (document.LineCount * 8));
            lineShifts = new int[document.LineCount];
            for (int index = 0; index < document.LineCount; index++) {
                HighlightedLine line = document.Lines.Span[index];
                LineNumberGutter.Append(builder, document.GetLineNumber(index), document.LineNumberTotal);
                lineShifts[index] = builder.Length - line.Start;
                int end = index + 1 < document.LineCount ? document.Lines.Span[index + 1].Start : document.Text.Length;
                builder.Append(document.Text.AsSpan(line.Start, end - line.Start));
            }
            text = builder.ToString();
        }
        int lineIndex = 0;
        for (int i = 0; i < ranges.Length; i++) {
            StyledRange range = ranges[i];
            while (lineIndex + 1 < document.LineCount && range.Start >= document.Lines.Span[lineIndex + 1].Start)
                lineIndex++;
            Style style = Style.None;
            if (range.Style.Foreground >= 0)
                style = style.WithForeground(ToColor(range.Style.Foreground));
            if (range.Style.Background >= 0)
                style = style.WithBackground(ToColor(range.Style.Background));
            TextStyle decoration = TextStyle.None;
            if ((range.Style.Decoration & TextDecoration.Bold) != 0)
                decoration |= TextStyle.Bold;
            if ((range.Style.Decoration & TextDecoration.Dim) != 0)
                decoration |= TextStyle.Dim;
            if ((range.Style.Decoration & (TextDecoration.SlowBlink | TextDecoration.RapidBlink)) != 0)
                decoration |= TextStyle.Blink;
            if ((range.Style.Decoration & TextDecoration.Invert) != 0)
                decoration |= TextStyle.Invert;
            if ((range.Style.Decoration & TextDecoration.Conceal) != 0)
                decoration |= TextStyle.Hidden;
            if ((range.Style.Decoration & TextDecoration.Italic) != 0)
                decoration |= TextStyle.Italic;
            if ((range.Style.Decoration & TextDecoration.Underline) != 0)
                decoration |= TextStyle.Underline;
            if ((range.Style.Decoration & TextDecoration.Strikethrough) != 0)
                decoration |= TextStyle.Strikethrough;
            runs[i] = new(range.Start + (lineShifts?[lineIndex] ?? 0), range.Length, style.WithTextStyle(decoration));
            if (!string.IsNullOrEmpty(range.Style.Hyperlink)) {
                hyperlinks ??= [];
                hyperlinks.Add(new(range.Start + (lineShifts?[lineIndex] ?? 0), range.Length, range.Style.Hyperlink));
            }
        }
        // XenoAtom treats Wrap=false as a single-line control, including when Text contains LF.
        // The core already lays out the document; the wrapped native path preserves those lines.
        return new Paragraph(text) { Runs = runs, Hyperlinks = hyperlinks?.ToArray() ?? [], Wrap = true };
    }

    // Palette references stay palette colors so the user's terminal theme applies.
    private static Color ToColor(int value) => value >= 0x1000000
        ? (value & 0xff) < 16 ? Color.Basic16(value & 0xff) : Color.Indexed256(value & 0xff)
        : Color.Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
}

/// <summary>Native XenoAtom visual which shares TextMate's width-dependent layout.</summary>
public sealed class TextMateVisual : ContentVisual, ITextMateOutput {
    private readonly TextMateView _view;
    private readonly int _preferredWidth;
    public HighlightedDocument Document { get; private set; }
    public Paragraph Paragraph => (Paragraph)Content!;

    internal TextMateVisual(HighlightedDocument document, RendererOptions options) {
        _view = new TextMateView(document, options);
        _preferredWidth = options.Width > 0 ? options.Width : 80;
        Document = _view.Layout(_preferredWidth);
        Content = Renderer.BuildParagraph(Document);
    }

    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        HighlightedDocument document = _view.Layout(Math.Max(1, constraints.IsWidthBounded ? constraints.MaxWidth : _preferredWidth));
        if (!ReferenceEquals(document, Document)) {
            Document = document;
            Content = Renderer.BuildParagraph(document);
        }
        return base.MeasureCore(in constraints);
    }
}
