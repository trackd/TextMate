using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class PagerSpoolImageTests {
    private static HighlightedDocument Create(string payload) {
        var builder = new PresentedTextBuilder();
        builder.Append("abcdefgh\n", TextStyle.Plain);
        builder.AddImage(builder.Length, new TerminalImage(3, 2, payload.AsMemory()));
        builder.Append("▀▀▀\n▀▀▀\nafter", TextStyle.Plain);
        return builder.Build(SyntaxHighlighter.PlainText("source"));
    }

    [Fact]
    public void Spool_RetainsMultipleImagePayloadsAndReindexesAnchorsWithoutSplittingFootprints() {
        using var spool = new PagerSpool();
        spool.SetWidth(20);
        spool.Append(Create("first"));
        spool.Append(Create("second"));
        Assert.Equal(2, spool.ImageCount);
        Assert.Equal(new TextBounds(0, 1, 3, 2), spool.GetImageBounds(0));
        Assert.Equal(new TextBounds(0, 5, 3, 2), spool.GetImageBounds(1));
        Assert.Equal("first", spool.ReadImage(0).Sixel.ToString());
        Assert.Equal("second", spool.ReadImage(1).Sixel.ToString());
        spool.SetWidth(2);
        Assert.Equal(new TextBounds(0, 4, 3, 2), spool.GetImageBounds(0));
        Assert.Equal(new TextBounds(0, 13, 3, 2), spool.GetImageBounds(1));
        Assert.Equal(3, spool.ReadLine(4).Line.Length);
        Assert.Equal(3, spool.ReadLine(5).Line.Length);
        spool.SetWidth(20);
        Assert.Equal(8, spool.LineCount);
        Assert.Equal(new TextBounds(0, 5, 3, 2), spool.GetImageBounds(1));
        Assert.Equal("first", spool.ReadImage(0).Sixel.ToString());
    }

    [Fact]
    public void RetainedImageCapability_ChangesOnlyAfterImagesArriveWithoutTerminalQueries() {
        using var spool = new PagerSpool();
        var terminal = new PagerTerminal(retainedImages: () => spool.ImageCount > 0);
        Assert.False(PagerTerminal.CanDraw(terminal));
        spool.Append("plain", TextStyle.Plain);
        Assert.False(PagerTerminal.CanDraw(terminal));
        spool.Append(Create("pixels"));
        Assert.True(PagerTerminal.CanDraw(terminal));
        Assert.Equal(default, terminal.Graphics);
    }

    [Fact]
    public void Spool_ImageCacheIsBoundedAndEveryOwnedFileIsDeleted() {
        var spool = new PagerSpool();
        spool.Append(Create(new string('x', 2_100_000)));
        string path = Assert.IsType<string>(spool.ImageHistoryPath);
        Assert.True(File.Exists(path));
        Assert.Equal(2_100_000, spool.ReadImage(0).Sixel.Length);
        Assert.Equal(0, spool.CachedImageBytes);
        spool.Append(Create("small"));
        TerminalImage cached = spool.ReadImage(1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++) {
            Assert.Same(cached, spool.ReadImage(1));
            spool.GetImageBounds(1);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(10, spool.CachedImageBytes);
        spool.Dispose();
        Assert.False(File.Exists(path));
        Assert.Throws<ObjectDisposedException>(() => spool.ReadImage(0));
    }

    [Fact]
    public void StreamingImages_DrawOriginalPayloadAndSkipImageInteriorOnScroll() {
        string payload = VT.SixelStart + "\"1;1;6;8#1;2;100;0;0!6~" + VT.StringTerminator;
        using var spool = new PagerSpool();
        spool.Append(Create(payload));
        spool.Append(Create(payload));
        var state = new PagerState(spool) { KeepImagesWhole = true };
        state.Resize(20, 7);
        using var frame = new PagerFrame();
        var terminal = new CaptureTerminal();
        frame.Draw(state, terminal);
        Assert.Contains(payload, terminal.Output.ToString());
        Assert.DoesNotContain('▀', terminal.Output.ToString());
        state.Handle(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.Equal(1, state.Top);
        state.Handle(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.Equal(3, state.Top);
        terminal.Output.Clear();
        frame.Draw(state, terminal);
        Assert.Contains(payload, terminal.Output.ToString());
        Assert.DoesNotContain('▀', terminal.Output.ToString());
    }

    private sealed class CaptureTerminal : IPagerTerminal {
        internal readonly System.Text.StringBuilder Output = new();
        public TerminalGraphics Graphics => new(true, 2, 4);
        public int Width => 20;
        public int Height => 7;
        public void Begin() {

        }
        public void End() {

        }
        public bool TryReadKey(out ConsoleKeyInfo key) {
            key = default;
            return false;
        }
        public void Write(ReadOnlySpan<char> text) => Output.Append(text);
        public void Flush() {

        }
        public void Idle() {

        }
    }
}
