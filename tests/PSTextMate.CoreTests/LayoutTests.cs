using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class LayoutTests {
    private static readonly string LongLine = "$x = " + string.Join(" + ", Enumerable.Repeat("'word'", 12));

    [Fact]
    public void LineNumbers_IdentifySourceLinesAndLeaveWrappedRowsBlank() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("# short\n" + LongLine + "\nGet-Date", "powershell", "DarkPlus", lineNumbers: true);
        HighlightedDocument laid = DocumentPresentation.Prepare(document, new RendererOptions(Width: 30));
        string[] rows = TextRenderer.FormatText(laid, ansi: false).Split('\n');

        Assert.True(rows.Length > 3);
        Assert.StartsWith("1 │ # short", rows[0]);
        Assert.StartsWith("2 │ $x", rows[1]);
        for (int row = 2; row < rows.Length - 1; row++)
            Assert.StartsWith("  │ ", rows[row]);
        Assert.Equal("3 │ Get-Date", rows[^1]);
        Assert.Equal(3, laid.LineNumberTotal);
        Assert.Equal(0, laid.GetLineNumber(2));
    }

    [Fact]
    public void LineNumbers_GutterWidthDependsOnSourceLinesOnly() {
        string source = string.Join('\n', Enumerable.Repeat(LongLine, 9));
        HighlightedDocument document = SyntaxHighlighter.Highlight(source, "powershell", "DarkPlus", lineNumbers: true);
        HighlightedDocument laid = DocumentPresentation.Prepare(document, new RendererOptions(Width: 30));

        Assert.True(laid.LineCount >= 10);
        Assert.All(TextRenderer.FormatText(laid, ansi: false).Split('\n'), row => Assert.Equal('│', row[2]));
    }

    [Fact]
    public void Display_LaysOutAtTheWidthAvailableWhenShown() {
        HighlightedDocument document = SyntaxHighlighter.Highlight(LongLine, "powershell", "DarkPlus");
        var output = (HighlightedDocument)OutputRenderer.Render(document, OutputFormat.Text, new RendererOptions(Width: 40, ConstrainWidth: false));

        Assert.True(output.LineCount > 1);
        Assert.Equal(1, OutputRenderer.LayoutForDisplay(output, 200).LineCount);
        Assert.Same(output, OutputRenderer.LayoutForDisplay(output, 40));
        Assert.DoesNotContain('\n', TextRenderer.FormatText(OutputRenderer.LayoutForDisplay(output, 200), ansi: false));
    }

    [Fact]
    public void Display_ExplicitWidthCapsTheAvailableWidth() {
        HighlightedDocument document = SyntaxHighlighter.Highlight(LongLine, "powershell", "DarkPlus");
        var output = (HighlightedDocument)OutputRenderer.Render(document, OutputFormat.Text, new RendererOptions(Width: 40, ConstrainWidth: true));

        HighlightedDocument displayed = OutputRenderer.LayoutForDisplay(output, 200);
        Assert.True(displayed.LineCount > 1);
        foreach (HighlightedLine line in displayed.Lines.Span)
            Assert.True(TerminalCells.Measure(displayed.Text.AsSpan(line.Start, line.Length)) <= 40);
    }

    [Fact]
    public void Tabs_AdvanceToTheNextTabStop() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("a\tb\n\tc\nabcd\te", "plaintext", "DarkPlus");
        Assert.Equal("a   b\n    c\nabcd    e", DocumentPresentation.Prepare(document).Text);
    }
}
