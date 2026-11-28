using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class TerminalImageOutputTests {
    private const string Payload = "\u001bP0;1q\"1;1;6;8#1;2;100;0;0!6~\u001b\\";

    private static HighlightedDocument ImageDocument(bool trailingNewline = true) {
        var builder = new PresentedTextBuilder();
        builder.Append("before\n", TextStyle.Plain);
        builder.AddImage(builder.Length, new TerminalImage(3, 2, Payload.AsMemory()));
        builder.Append("▀▀▀\n▀▀▀" + (trailingNewline ? "\nafter" : ""), TextStyle.Plain);
        return builder.Build(SyntaxHighlighter.PlainText("image source"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GraphicsOutput_WritesOriginalPixelsReservesRowsAndRestoresCursorAndModes(bool newline) {
        HighlightedDocument document = ImageDocument(newline);
        using var writer = new TextBufferWriter();
        TextRenderer.Write(document, writer, graphics: true);
        string output = writer.ToString();
        Assert.Contains(Payload, output);
        Assert.DoesNotContain('▀', output);
        Assert.Contains("before", output);
        Assert.Equal(2, output.Split(Payload, StringSplitOptions.None).Length);
        Assert.True(output.IndexOf('\n', output.IndexOf("before", StringComparison.Ordinal)) < output.IndexOf(Payload, StringComparison.Ordinal));
        Assert.Contains(VT.SaveSixelModes, output);
        Assert.Contains(VT.RestoreSixelModes + VT.RestoreCursor, output);
        Assert.Contains(newline ? "\u001b[2A" : "\u001b[1A", output);
        Assert.Equal("image source", document.Source);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void PortableOrPlainOutput_RetainsCellFallbackWithoutTerminalGraphics(bool graphics, bool ansi) {
        using var writer = new TextBufferWriter();
        TextRenderer.Write(ImageDocument(), writer, ansi, graphics: graphics);
        Assert.DoesNotContain(Payload, writer.ToString());
        Assert.Contains('▀', writer.ToString());
    }

    [Fact]
    public void PartialImageRange_UsesCellsWithoutEmittingAnIncompleteImage() {
        using var writer = new TextBufferWriter();
        TextRenderer.Write(ImageDocument(), writer, startLine: 2, count: 1, graphics: true);
        Assert.DoesNotContain(Payload, writer.ToString());
        Assert.Contains('▀', writer.ToString());
    }
}
