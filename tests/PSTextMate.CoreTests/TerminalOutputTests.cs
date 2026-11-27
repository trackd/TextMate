using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class TerminalOutputTests {
    [Fact]
    public void Parse_StylesAndResetsCapturedTextWithoutExecutingCursorOrOscCommands() {
        string input = "\u001b[2J\u001b]0;unwanted title\a\u001b[1;38;2;12;34;56;48;5;196mred\u001b[22;39;49mplain";
        HighlightedDocument result = VT.Parse(input);
        Assert.Equal(input, result.Source);
        Assert.Equal("redplain", result.Text);
        StyledRange[] ranges = result.Ranges.ToArray();
        Assert.Equal(new TextStyle(0x0c2238, 0xff0000, TextDecoration.Bold), ranges[0].Style);
        Assert.Equal(new TextStyle(-1, -1, TextDecoration.None), ranges[1].Style);
        Assert.Equal(3, ranges[0].Length);
        Assert.DoesNotContain("2J", result.ToAnsi());
    }

    [Theory]
    [InlineData("PaletteCube", 0x5f87af)]
    [InlineData("PaletteGray", 0x080808)]
    [InlineData("ColonTruecolor", 0x010203)]
    [InlineData("ColonDefaultColorSpace", 0x010203)]
    [InlineData("C1Sgr", 0xff0000)]
    public void Parse_DecodesPaletteTruecolorAndC1Sgr(string scenario, int expected) {
        string sequence = scenario switch {
            "PaletteCube" => "\u001b[38;5;67m",
            "PaletteGray" => "\u001b[38;5;232m",
            "ColonTruecolor" => "\u001b[38:2::1:2:3m",
            "ColonDefaultColorSpace" => "\u001b[38:2:0:1:2:3m",
            "C1Sgr" => "\u009b91m",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        HighlightedDocument result = VT.Parse(sequence + "x");
        Assert.Equal("x", result.Text);
        Assert.Equal(expected, result.Ranges.Span[0].Style.Foreground);
    }

    [Fact]
    public void Parse_PreservesLinksAcrossSgrResetAndClosesThemAtOscTerminator() {
        HighlightedDocument result = VT.Parse("\u001b]8;id=doc;https://example.com\u001b\\a\u001b[0mb\u001b]8;;\ac");
        Assert.Equal("abc", result.Text);
        Assert.Equal("https://example.com", result.Ranges.Span[0].Style.Hyperlink);
        Assert.Equal(2, result.Ranges.Span[0].Length);
        Assert.Null(result.Ranges.Span[1].Style.Hyperlink);
        Assert.Contains("href=\"https://example.com\"", result.ToHtml());
    }

    [Fact]
    public void Parse_RestoresExtendedDecorationsAndIndividualResets() {
        HighlightedDocument result = VT.Parse("\u001b[1;2;3;4;5;6;7;8;9mA\u001b[22;23;24;25;27;28;29mB");
        TextDecoration all = TextDecoration.Bold | TextDecoration.Dim | TextDecoration.Italic | TextDecoration.Underline
            | TextDecoration.SlowBlink | TextDecoration.RapidBlink | TextDecoration.Invert | TextDecoration.Conceal | TextDecoration.Strikethrough;
        Assert.Equal(all, result.Ranges.Span[0].Style.Decoration);
        Assert.Equal(TextDecoration.None, result.Ranges.Span[1].Style.Decoration);
        Assert.Contains(";2", result.ToAnsi());
        Assert.Contains(";7", result.ToAnsi());
        Assert.Contains("visibility:hidden", result.ToHtml());
    }

    [Fact]
    public void Parser_RetainsStyleAcrossRecordsAndReflowsWithoutReparsingEscapes() {
        var parser = new VTParser();
        parser.Append("\u001b[32mfirst\n");
        parser.Append("second long line\u001b[0m");
        HighlightedDocument document = parser.Build("original ANSI input");
        var view = new TextMateView(document);
        HighlightedDocument narrow = view.Layout(8);
        Assert.True(narrow.LineCount > document.LineCount);
        Assert.All(narrow.Ranges.ToArray(), range => Assert.Equal(0x008000, range.Style.Foreground));
        Assert.Equal("first\nsecond long line", view.Layout(80).Text);
    }

    [Fact]
    public void Parse_TreatsDecorationSubparametersAsOneCommandAndEmptySgrAsReset() {
        HighlightedDocument result = VT.Parse("\u001b[4:2mA\u001b[mB");
        Assert.Equal(TextDecoration.Underline, result.Ranges.Span[0].Style.Decoration);
        Assert.Equal(TextDecoration.None, result.Ranges.Span[1].Style.Decoration);
    }

    [Fact]
    public void Parse_ExpandsTabsAtCellStopsAndDiscardsIncompleteOrOversizedCommands() {
        HighlightedDocument result = VT.Parse("界\tX\r\n\u001bPignored\u001b\\end\u001b[999999999999999999999m!\u001b]8;;unterminated");
        Assert.Equal("界      X\nend!", result.Text);
        Assert.DoesNotContain('\u001b', result.Text);
        Assert.Equal(-1, result.Ranges.Span[0].Style.Foreground);
    }
}
