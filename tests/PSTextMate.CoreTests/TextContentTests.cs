using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class TextContentTests {
    private sealed class CustomRenderable : ITextRenderable {
        public HighlightedDocument Layout(int width) => SyntaxHighlighter.PlainText(new string('x', width));
    }

    [Fact]
    public void Renderable_ComposesCustomWidthAwareSources() {
        var content = TextContent.FromRenderable(new CustomRenderable());
        Assert.Equal(new string('x', 12), content.Layout(12).Text);
        Assert.Same(content.Layout(12), content.Layout(12));
        Assert.Equal(new string('x', 18), content.Layout(18).Text);
        Assert.Contains("xxxxxxxxxxxx", content.ToHtml(width: 12));
    }
    [Fact]
    public void Colors_DefaultInheritsAndHexParsingPreservesRgbWithoutCulture() {
        Assert.Equal(-1, default(TextColor).Rgb);
        Assert.True(TextColor.Default.IsDefault);
        Assert.Equal(0xaabbcc, TextColor.Parse("#AbC").Rgb);
        Assert.Equal(TextColor.FromRgb(12, 34, 56), TextColor.Parse("#0c2238"));
        Assert.Equal("#0C2238", TextColor.FromRgb(12, 34, 56).ToString());
        Assert.Throws<FormatException>(() => TextColor.Parse("#-12345"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextColor.FromRgb(0x1000000));
        Assert.Equal(-1, new TextStyle(TextColor.Red).Background);
    }

    [Fact]
    public void Panel_ComposesStyledTextHeaderFooterAndPaddingWithExactCellWidth() {
        TextStyle style = new(TextColor.Cyan, decoration: TextDecoration.Bold);
        TextContent panel = TextContent.Text("界 hello", style).Panel(new PanelOptions {
            Header = "Title",
            Footer = "End",
            Border = PanelBorder.DoubleLine,
            TitleStyle = new TextStyle(TextColor.Yellow),
            VerticalPadding = 1
        });
        HighlightedDocument result = panel.Layout(18);
        Assert.Equal("╔═Title══════════╗", result.Text.Split('\n')[0]);
        Assert.EndsWith("╚═End════════════╝", result.Text);
        Assert.Equal(5, result.LineCount);
        Assert.All(result.Text.Split('\n'), row => Assert.Equal(18, TerminalCells.Measure(row)));
        StyledRange text = Assert.Single(result.Ranges.ToArray().Where(range => result.Text.Substring(range.Start, range.Length) == "界 hello"));
        Assert.Equal(style, text.Style);
        Assert.Contains(result.Ranges.ToArray(), range => range.Style.Foreground == TextColor.Yellow.Rgb);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(40)]
    public void PanelsAndColumns_StayInsideNarrowWidthsAndPreserveGraphemes(int width) {
        var content = TextContent.Columns(2,
            new TextColumn(TextContent.Text("🦊 e\u0301 words").Panel("界🦊 title")),
            new TextColumn(TextContent.Text("right").Pad(2).Panel("right")));
        HighlightedDocument document = content.Layout(width);
        Assert.All(document.Text.Split('\n'), line => Assert.Equal(width, TerminalCells.Measure(line)));
        Assert.DoesNotContain('\ufffd', document.Text);
    }

    [Fact]
    public void Composition_RetainsResponsiveLayoutWhenDocumentIsReusedAtAnotherWidth() {
        TextContent content = TextContent.Text("Some words that should wrap inside a titled panel.").Panel("Example");
        HighlightedDocument document = content.ToDocument(60);
        var view = new TextMateView(document);
        HighlightedDocument narrow = view.Layout(20);
        Assert.True(narrow.LineCount > document.LineCount);
        Assert.All(narrow.Text.Split('\n'), line => Assert.Equal(20, TerminalCells.Measure(line)));
        Assert.Equal(document.Text, view.Layout(60).Text);
        Assert.Same(view.Layout(60), view.Layout(60));
    }

    [Fact]
    public void UnchangedComposedView_ReusesLayoutWithoutAllocating() {
        var view = new TextMateView(TextContent.Text("cached output").Panel("Cached").ToDocument(40));
        for (int index = 0; index < 100; index++)
            view.Layout(40);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
            view.Layout(40);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Columns_ReserveFixedWidthAndAlignRowsWhileKeepingLinksAndSourceStyles() {
        var content = TextContent.Columns(1,
            new TextColumn(TextContent.FromAnsi("\u001b]8;;https://example.com\a\u001b[31mlink\u001b]8;;\a")),
            new TextColumn(TextContent.Text("42").Align(TextAlignment.Right), 5));
        HighlightedDocument document = content.Layout(16);
        Assert.Equal("link          42", document.Text);
        Assert.Equal("https://example.com", document.Ranges.Span[0].Style.Hyperlink);
        Assert.Contains("href=\"https://example.com\"", document.ToHtml());
    }

    [Fact]
    public void LineNumbers_ExpandDigitWidthAndAreSharedWithStreamingGutters() {
        HighlightedDocument document = TextContent.Text("first\nsecond\nthird").LineNumbers(9).Layout(15);
        Assert.Equal(" 9 │ first     \n10 │ second    \n11 │ third     ", document.Text);
        using var writer = new StringWriter();
        LineNumberGutter.Write(writer, 9, 11);
        Assert.Equal(" 9 │ ", writer.ToString());
        Assert.Equal(writer.ToString(), LineNumberGutter.Format(9, 11));
        Assert.False(document.ShowLineNumbers);
    }

    [Fact]
    public void PanelTitles_ShowControlsLiterallyAndNeverWriteTerminalInstructions() {
        const string title = "bad\u001b[2J\n";
        string border = PanelFrame.Border(30, title);
        Assert.DoesNotContain('\u001b', border);
        Assert.DoesNotContain('\n', border);
        Assert.Contains('␛', border);
        HighlightedDocument document = TextContent.Text("body").Panel(title).Layout(30);
        Assert.DoesNotContain('\u001b', document.Text);
        Assert.All(document.Text.Split('\n'), row => Assert.Equal(30, TerminalCells.Measure(row)));
    }

    [Fact]
    public void StyledComposition_InheritsChildColorsAndAddsDecorationsAcrossPadding() {
        TextContent content = TextContent.Text("styled", new TextStyle(TextColor.Red)).Pad(1)
            .WithStyle(TextStyle.Plain.WithBackground(TextColor.Blue).WithDecoration(TextDecoration.Underline));
        HighlightedDocument document = content.Layout(12);
        Assert.All(document.Ranges.ToArray(), range => {
            Assert.Equal(TextColor.Blue.Rgb, range.Style.Background);
            Assert.Equal(TextDecoration.Underline, range.Style.Decoration);
        });
        Assert.Contains(document.Ranges.ToArray(), range => range.Style.Foreground == TextColor.Red.Rgb);
        Assert.Throws<ArgumentException>(() => TextStyle.Plain.WithHyperlink("unsafe\u001b[2J"));
    }

    [Fact]
    public void StreamingAndMaterializedPanelBorders_UseTheSameCellGeometry() {
        foreach (int width in new[] { 0, 1, 2, 3, 8, 24 }) {
            foreach (TextAlignment alignment in Enum.GetValues<TextAlignment>()) {
                using var writer = new StringWriter();
                PanelFrame.WriteBorder(writer, width, "界🦊e\u0301", border: PanelBorder.DoubleLine, alignment: alignment);
                string border = PanelFrame.Border(width, "界🦊e\u0301", border: PanelBorder.DoubleLine, alignment: alignment);
                Assert.Equal(border, writer.ToString());
                Assert.Equal(width, TerminalCells.Measure(border));
            }
        }
    }

    [Fact]
    public void RulesProgressAndRows_ComposeWithoutFrameworkDependencies() {
        var content = TextContent.Rows(TextContent.Rule("Load"), TextContent.Progress(1, 4), TextContent.Text("ready").Align(TextAlignment.Center));
        Assert.Equal("─── Load ───\n███         \n   ready    ", content.Layout(12).Text);
        Assert.Equal(0, TextProgress.Filled(20, 5, 0));
        Assert.Equal(20, TextProgress.Filled(20, long.MaxValue, long.MaxValue));
    }
}
