using System.Buffers.Binary;
using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

[Collection(nameof(TerminalCapabilitiesTests))]
public sealed class TerminalImageTests {
    private const string ST = "\u001b\\";
    private static readonly TerminalImageParser.CellMetrics Cells = new(10, 20, 80, 24);

    /// <summary>The 24 header bytes of a PNG with the given size, as base64.</summary>
    private static string PngHeader(int width, int height) {
        byte[] header = new byte[24];
        BinaryPrimitives.WriteUInt64BigEndian(header, 0x89504e470d0a1a0aUL);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 13);
        "IHDR"u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), height);
        return Convert.ToBase64String(header);
    }

    private static TerminalImage Read(string sequence, out int consumed) {
        Assert.True(TerminalImageParser.TryRead(sequence, Cells, out consumed, out TerminalImage? image));
        return image!;
    }

    private static bool Rejects(string sequence) => !TerminalImageParser.TryRead(sequence, Cells, out _, out _);

    [Fact]
    public void Sixel_UsesRasterAttributesOrMeasuresBands() {
        string raster = "\u001bP0;1q\"1;1;20;36#0;2;0;0;0#0~~" + ST;
        TerminalImage image = Read(raster + "tail", out int consumed);
        Assert.Equal((TerminalImageProtocol.Sixel, 2, 2), (image.Protocol, image.Width, image.Height));
        Assert.Equal(raster.Length, consumed);
        Assert.Equal(raster, image.Payload.ToString());

        image = Read("\u001bPq#0!30~-!25~" + ST, out _);
        Assert.Equal((3, 1), (image.Width, image.Height));
    }

    [Fact]
    public void Kitty_JoinsChunksForcesQuietRepliesAndReadsCells() {
        string sequence = "\u001b_Ga=T,f=100,c=4,r=3,m=1;" + PngHeader(8, 8) + ST + "\u001b_Gm=0;AAAA" + ST;
        TerminalImage image = Read(sequence + "\r\n", out int consumed);
        Assert.Equal((TerminalImageProtocol.Kitty, 4, 3), (image.Protocol, image.Width, image.Height));
        Assert.Equal(sequence.Length, consumed);
        string payload = image.Payload.ToString();
        Assert.Equal(2, payload.Split("q=2").Length - 1);
        Assert.EndsWith(";AAAA" + ST, payload);
    }

    [Fact]
    public void Kitty_MeasuresThePngHeaderWhenNoCellsAreGiven() {
        TerminalImage image = Read("\u001b_Ga=T,f=100;" + PngHeader(40, 60) + ST, out _);
        Assert.Equal((4, 3), (image.Width, image.Height));
        image = Read("\u001b_Ga=T,f=100,c=8;" + PngHeader(40, 60) + ST, out _);
        Assert.Equal((8, 6), (image.Width, image.Height));
    }

    [Theory]
    [InlineData("\u001b_Ga=T,t=f,c=1,r=1;L3RtcC94" + ST)]
    [InlineData("\u001b_Ga=q,i=1,c=1,r=1;AAAA" + ST)]
    [InlineData("\u001b_Ga=t,i=1,c=1,r=1;AAAA" + ST)]
    [InlineData("\u001b_Ga=T,U=1,i=1,c=1,r=1;AAAA" + ST)]
    [InlineData("\u001b_Ga=d,d=A" + ST)]
    [InlineData("\u001b_Ga=T,c=1,r=1,m=1;AAAA" + ST)]
    public void Kitty_RejectsFilesQueriesStateChangesAndIncompleteTransfers(string sequence) => Assert.True(Rejects(sequence));

    [Fact]
    public void InlineImage_ReadsCellsPixelsPercentAndHeaderSizes() {
        string data = PngHeader(100, 40);
        Assert.Equal((5, 2), Size(Read("\u001b]1337;File=inline=1;width=5;height=2:" + data + "\a", out int consumed)));
        Assert.Equal("\u001b]1337;File=inline=1;width=5;height=2:".Length + data.Length + 1, consumed);
        Assert.Equal((10, 2), Size(Read("\u001b]1337;File=inline=1:" + data + ST, out _)));
        Assert.Equal((4, 1), Size(Read("\u001b]1337;File=inline=1;width=40px:" + data + "\a", out _)));
        Assert.Equal((40, 8), Size(Read("\u001b]1337;File=name=YS5wbmc=;size=24;inline=1;width=50%:" + data + "\a", out _)));
        Assert.Equal(TerminalImageProtocol.InlineImage, Read("\u001b]1337;File=inline=1:" + data + "\a", out _).Protocol);
    }

    [Theory]
    [InlineData("\u001b]1337;File=width=2;height=2:AAAA\a")]
    [InlineData("\u001b]1337;File=inline=0;width=2;height=2:AAAA\a")]
    [InlineData("\u001b]1337;File=inline=1;width=2;height=2:AAAA")]
    [InlineData("\u001b]1337;MultipartFile=inline=1\a")]
    public void InlineImage_RejectsDownloadsAndUnterminatedSequences(string sequence) => Assert.True(Rejects(sequence));

    [Fact]
    public void GoodImage_KeepsOneShotRendersWithoutReplyOrScrollRequests() {
        TerminalImage image = Read("\u001bP!go=s,f=4,c=4,r=2,s,l,u;!" + PngHeader(8, 8) + ST, out _);
        Assert.Equal((TerminalImageProtocol.GoodImage, 4, 2), (image.Protocol, image.Width, image.Height));
        Assert.StartsWith("\u001bP!go=s,f=4,c=4,r=2,u;!", image.Payload.ToString());
        Assert.Equal((2, 1), Size(Read("\u001bP!go=s,f=4;!" + PngHeader(20, 20) + ST, out _)));
        Assert.True(Rejects("\u001bP!go=u,n=pic,f=4;!" + PngHeader(8, 8) + ST));
        Assert.True(Rejects("\u001bP!go=r,n=pic,c=2,r=2" + ST));
    }

    [Fact]
    public void CapturedOutput_ReservesImageCellsAndKeepsThePayload() {
        string sixel = "\u001bP0;1q\"1;1;30;36#0~" + ST;
        HighlightedDocument document = VT.Parse("ab" + sixel + "cd\nnext");
        TerminalImage image = Assert.Single(document.Images.ToArray()).Image;
        Assert.Equal(sixel, image.Payload.ToString());
        Assert.Equal(new TextBounds(2, 0, 3, 2), document.GetImageBounds(0));
        // Text after the image continues to the right of its last row, where the terminal leaves the cursor.
        Assert.Equal("ab   \n     cd\nnext", document.Text);
    }

    [Fact]
    public void CapturedOutput_SkipsCursorRowsThatFollowAPlacement() {
        string kitty = "\u001b_Ga=T,f=100,c=2,r=3;" + PngHeader(8, 8) + ST;
        HighlightedDocument document = VT.Parse(kitty + "\r\n\r\n\r" + "after");
        Assert.Equal(3, document.LineCount);
        Assert.Equal("  after", document.Text.Split('\n')[^1]);
    }

    [Fact]
    public void CapturedOutput_KeepsLinksAndDropsUnrecognizedSequences() {
        HighlightedDocument document = VT.Parse("\u001b]8;;https://example.com\u001b\\x\u001b]8;;\u001b\\\u001bP$qm" + ST + "y");
        Assert.Equal("xy", document.Text);
        Assert.True(document.Images.IsEmpty);
    }

    [Fact]
    public void Spool_RetainsTheProtocolOfEachImage() {
        using var spool = new PagerSpool();
        spool.Append(VT.Parse("\u001b_Ga=T,f=100,c=2,r=1;" + PngHeader(8, 8) + ST));
        spool.Append(VT.Parse("\u001b]1337;File=inline=1;width=2;height=1:" + PngHeader(8, 8) + "\a"));
        Assert.Equal(TerminalImageProtocol.Kitty, spool.ReadImage(0).Protocol);
        Assert.Equal(TerminalImageProtocol.InlineImage, spool.ReadImage(1).Protocol);
    }

    [Fact]
    public void Pager_RemovesKittyPlacementsBeforeRedrawingAndOnExit() {
        string kitty = "\u001b_Ga=T,f=100,c=2,r=1;" + PngHeader(8, 8) + ST;
        using var spool = new PagerSpool();
        spool.Append(VT.Parse(kitty));
        spool.Append("text", TextStyle.Plain);
        var state = new PagerState(spool) { KeepImagesWhole = true };
        state.Resize(20, 7);
        using var frame = new PagerFrame();
        var terminal = new CaptureTerminal();
        frame.Draw(state, terminal);
        string first = terminal.Output.ToString();
        Assert.Contains("a=T", first);
        Assert.DoesNotContain(VT.DeleteKittyPlacements, first);
        Assert.True(frame.HasKittyPlacements);

        // A redraw removes the previous placement before placing the image again.
        terminal.Output.Clear();
        frame.Draw(state, terminal);
        string second = terminal.Output.ToString();
        int deleted = second.IndexOf(VT.DeleteKittyPlacements, StringComparison.Ordinal);
        Assert.True(deleted >= 0);
        Assert.True(second.IndexOf("a=T", StringComparison.Ordinal) > deleted);
    }

    [Fact]
    public void Pager_RemovesKittyPlacementsBeforeLeavingTheScreen() {
        using var spool = new PagerSpool();
        spool.Append(VT.Parse("\u001b_Ga=T,f=100,c=2,r=1;" + PngHeader(8, 8) + ST));
        spool.Complete();
        var terminal = new CaptureTerminal();
        terminal.Keys.Enqueue(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false));
        TerminalPager.Run(new PagerState(spool), terminal);
        string output = terminal.Output.ToString();
        Assert.EndsWith(VT.DeleteKittyPlacements + "<end>", output);
    }

    private static (int Width, int Height) Size(TerminalImage image) => (image.Width, image.Height);

    private sealed class CaptureTerminal : IPagerTerminal {
        internal readonly System.Text.StringBuilder Output = new();
        public TerminalGraphics Graphics => new(false, 10, 20, Kitty: true);
        public int Width => 20;
        public int Height => 7;
        public void Begin() {

        }
        internal readonly Queue<ConsoleKeyInfo> Keys = new();
        public void End() => Output.Append("<end>");
        public bool TryReadKey(out ConsoleKeyInfo key) => Keys.TryDequeue(out key);
        public void Write(ReadOnlySpan<char> text) => Output.Append(text);
        public void Flush() {

        }
        public void Idle() {

        }
    }
}
