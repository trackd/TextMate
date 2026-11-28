using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class ColorTests {
    [Theory]
    [InlineData("\u001b[31mx", 1)]
    [InlineData("\u001b[94mx", 12)]
    [InlineData("\u001b[38;5;208mx", 208)]
    public void CapturedPaletteColors_StayPaletteReferences(string text, int index) {
        HighlightedDocument document = VT.Parse(text);
        TextColor color = document.Ranges.Span[0].Style.ForegroundColor;

        Assert.True(color.IsPalette);
        Assert.Equal(index, color.PaletteIndex);
    }

    [Fact]
    public void PaletteColors_AreWrittenAsPaletteColorsAtEveryDepth() {
        TextStyle style = new(TextColor.FromPalette(1), TextColor.FromPalette(12));
        foreach (TerminalColorDepth depth in Enum.GetValues<TerminalColorDepth>()) {
            using var writer = new StringWriter();
            VT.WriteStyle(writer, style, depth);
            Assert.Equal("\u001b[0;31;104m", writer.ToString());
        }
    }

    [Theory]
    [InlineData(TerminalColorDepth.TrueColor, "\u001b[0;38;2;255;0;0m")]
    [InlineData(TerminalColorDepth.EightBit, "\u001b[0;38;5;196m")]
    [InlineData(TerminalColorDepth.Standard, "\u001b[0;91m")]
    public void RgbColors_AreReducedToTheTerminalDepth(TerminalColorDepth depth, string expected) {
        using var writer = new StringWriter();
        VT.WriteStyle(writer, new TextStyle(TextColor.Red), depth);
        Assert.Equal(expected, writer.ToString());
    }

    [Fact]
    public void ExtendedPaletteColors_AreReducedForSixteenColorTerminals() {
        using var writer = new StringWriter();
        VT.WriteStyle(writer, new TextStyle(TextColor.FromPalette(196)), TerminalColorDepth.Standard);
        Assert.Equal("\u001b[0;91m", writer.ToString());
    }

    [Theory]
    [InlineData("COLORTERM=truecolor;TERM=xterm", false, TerminalColorDepth.TrueColor)]
    [InlineData("COLORTERM=24bit", false, TerminalColorDepth.TrueColor)]
    [InlineData("TERM=xterm-256color", false, TerminalColorDepth.EightBit)]
    [InlineData("TERM=tmux-256color", false, TerminalColorDepth.EightBit)]
    [InlineData("TERM_PROGRAM=Apple_Terminal;TERM=xterm-256color", false, TerminalColorDepth.EightBit)]
    [InlineData("TERM=xterm", false, TerminalColorDepth.Standard)]
    [InlineData("TERM=xterm-kitty", false, TerminalColorDepth.TrueColor)]
    [InlineData("", true, TerminalColorDepth.TrueColor)]
    public void ColorDepth_IsDetectedFromTheEnvironment(string variables, bool windows, TerminalColorDepth expected) {
        Dictionary<string, string> environment = variables.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(static pair => pair.Split('=')).ToDictionary(static pair => pair[0], static pair => pair[1]);
        Assert.Equal(expected, VT.DetectColorDepth(name => environment.GetValueOrDefault(name), windows, new Version(10, 0, 26100)));
    }

    [Fact]
    public void PaletteColors_ParseFormatAndReportTheirStandardValue() {
        TextColor color = TextColor.Parse("palette:4");
        Assert.Equal(4, color.PaletteIndex);
        Assert.Equal("palette:4", color.ToString());
        Assert.Equal(0x000080, color.Rgb);
        Assert.Equal(-1, TextColor.Red.PaletteIndex);
        Assert.Equal(color, new TextStyle(color).ForegroundColor);
        Assert.Throws<ArgumentOutOfRangeException>(() => TextColor.FromPalette(256));
    }
}
