using System.Collections.Concurrent;
using System.Text;
using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class StreamingPagerTests {
    [Fact]
    public void Spool_PlainAppendUsesSpansWithoutIntermediateDocumentsOrAllocations() {
        using var spool = new PagerSpool();
        for (int index = 0; index < 100; index++)
            spool.Append("row", TextStyle.Plain);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
            spool.Append("row", TextStyle.Plain);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1100, spool.LineCount);
        Assert.Equal("row", spool.ReadLine(1099).Document.Text);
    }

    [Fact]
    public void Spool_PlainAppendPreservesEmptyLinesAndMultilineStyles() {
        using var spool = new PagerSpool();
        var style = new TextStyle(TextColor.Red);
        spool.Append("one\r\n\r\nthree\n", style);
        Assert.Equal(4, spool.LineCount);
        Assert.Equal("one", spool.ReadLine(0).Document.Text);
        Assert.Equal(string.Empty, spool.ReadLine(1).Document.Text);
        Assert.Equal("three", spool.ReadLine(2).Document.Text);
        Assert.Equal(string.Empty, spool.ReadLine(3).Document.Text);
        Assert.Equal(style, spool.ReadLine(2).Document.Ranges.Span[0].Style);
    }
    [Fact]
    public void Spool_BoundsDecodedCacheBytesForLargeLines() {
        using var spool = new PagerSpool();
        spool.SetWidth(1_000_000);
        HighlightedDocument document = SyntaxHighlighter.PlainText(new string('x', 500_000));
        for (int index = 0; index < 6; index++)
            spool.Append(document);
        for (int index = 0; index < 6; index++)
            Assert.Equal(500_000, spool.ReadLine(index).Line.Length);
        Assert.InRange(spool.CachedBytes, 0, 4 * 1024 * 1024);
        Assert.InRange(spool.CachedLines, 1, 4);
        Assert.Equal(500_000, spool.ReadLine(0).Line.Length);
    }
    [Fact]
    public void Spool_DisposeDeletesEveryOwnedHistoryFileAndIsIdempotent() {
        var spool = new PagerSpool();
        (string Data, string Logical, string Index) paths = spool.HistoryPaths;
        spool.Append(VT.Parse("history"));
        spool.Dispose();
        spool.Dispose();
        Assert.False(File.Exists(paths.Data));
        Assert.False(File.Exists(paths.Logical));
        Assert.False(File.Exists(paths.Index));
        Assert.Throws<ObjectDisposedException>(() => spool.Append(VT.Parse("late")));
    }

    [Fact]
    public void CachedSpoolReads_DoNotAllocate() {
        using var spool = new PagerSpool();
        spool.Append(VT.Parse("cached"));
        for (int index = 0; index < 100; index++)
            spool.ReadLine(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
            spool.ReadLine(0);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Spool_RetainsHistoryAndStylesWithBoundedDecodedCache() {
        using var spool = new PagerSpool();
        for (int index = 0; index < 200; index++)
            spool.Append(VT.Parse($"\u001b[38;2;12;34;56mrow {index}"));
        Assert.Equal(200, spool.LineCount);
        for (int index = 0; index < 200; index++) {
            PagerLine row = spool.ReadLine(index);
            Assert.Equal($"row {index}", row.Document.Text);
            Assert.Equal(0x0c2238, row.Document.Ranges.Span[0].Style.Foreground);
        }
        Assert.Equal(64, spool.CachedLines);
        Assert.Equal("row 0", spool.ReadLine(0).Document.Text);
        spool.Complete();
        Assert.True(spool.IsComplete);
    }

    [Fact]
    public void Spool_ReindexesOriginalLinesOnResizeWithoutSplittingClusters() {
        using var spool = new PagerSpool();
        spool.Append(VT.Parse("\u001b[31mab界cd"));
        spool.SetWidth(4);
        Assert.Equal(2, spool.LineCount);
        PagerLine first = spool.ReadLine(0);
        Assert.Equal("ab界", first.Document.Text.Substring(first.Line.Start, first.Line.Length));
        PagerLine second = spool.ReadLine(1);
        Assert.Equal("cd", second.Document.Text.Substring(second.Line.Start, second.Line.Length));
        Assert.Equal(1, spool.LineAt(second.Offset + second.Line.Start));
        spool.SetWidth(20);
        Assert.Equal(1, spool.LineCount);
        Assert.Equal("ab界cd", spool.ReadLine(0).Document.Text);
    }

    [Fact]
    public void State_SearchesOldDiskRowsAndRefreshesArrivingInput() {
        using var spool = new PagerSpool();
        spool.Append(VT.Parse("old needle"));
        var state = new PagerState(spool);
        state.Resize(40, 8);
        for (int index = 0; index < 200; index++)
            spool.Append(VT.Parse($"line {index}"));
        state.RefreshSource();
        state.SetQuery("needle");
        Assert.Single(state.Hits);
        Assert.Equal(0, state.Hits[0].Line);
        spool.Append(VT.Parse("new needle"));
        state.RefreshSource();
        Assert.Equal(2, state.Hits.Count);
        Assert.Equal(201, state.Hits[1].Line);
        state.Handle(new ConsoleKeyInfo('F', ConsoleKey.F, true, false, false));
        spool.Append(VT.Parse("latest"));
        state.RefreshSource();
        Assert.True(state.Follow);
        Assert.Equal(state.LineCount - state.ContentRows, state.Top);
    }

    [Fact]
    public void Frame_DrawsWrappedRangesAndHighlightsWithoutLosingStyle() {
        using var spool = new PagerSpool();
        spool.Append(VT.Parse("\u001b[38;2;12;34;56mabcdefneedle"));
        var state = new PagerState(spool);
        state.Resize(9, 6);
        state.SetQuery("needle");
        var terminal = new ControlledTerminal();
        using var frame = new PagerFrame();
        frame.Draw(state, terminal);
        Assert.True(terminal.Contains("38;2;12;34;56"));
        Assert.True(terminal.Contains("cdef") || terminal.Contains("abcdefne"));
        Assert.Equal(2, state.LineCount);
    }

    [Fact]
    public void Session_PropagatesTerminalFailureAndStillCallsEnd() {
        var terminal = new ControlledTerminal { FailWrites = true };
        var session = new StreamingPager(CancellationToken.None, terminal);
        try {
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref terminal.Ends) == 1, TimeSpan.FromSeconds(5)));
            Assert.Throws<IOException>(() => session.Append("late"));
        }
        finally {
            session.Dispose();
        }
        session.Dispose();
        Assert.Equal(1, terminal.Ends);
    }

    [Fact]
    public void Session_DisplaysInputBeforeCompletionAndRestoresTerminalOnQuit() {
        var terminal = new ControlledTerminal();
        using var session = new StreamingPager(CancellationToken.None, terminal);
        session.Append("stream alpha");
        Assert.True(SpinWait.SpinUntil(() => terminal.Contains("stream alpha"), TimeSpan.FromSeconds(5)));
        session.Append("stream beta");
        Assert.True(SpinWait.SpinUntil(() => terminal.Contains("stream beta"), TimeSpan.FromSeconds(5)));
        terminal.Keys.Enqueue(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false));
        session.Complete();
        Assert.Equal(1, terminal.Ends);
    }

    [Fact]
    public void Session_StreamsMultipleRichImagesBeforeEofAndRestoresTerminalOnQuit() {
        var terminal = new ControlledTerminal { Graphics = new(true, 2, 4) };
        using var session = new StreamingPager(CancellationToken.None, terminal);
        static HighlightedDocument image(string marker) {
            var builder = new PresentedTextBuilder();
            builder.Append("before\n", TextStyle.Plain);
            builder.AddImage(builder.Length, new TerminalImage(3, 2,
                (VT.SixelStart + marker + VT.StringTerminator).AsMemory()));
            builder.Append("▀▀▀\n▀▀▀\nafter", TextStyle.Plain);
            return builder.Build(SyntaxHighlighter.PlainText("source"));
        }
        session.Append(image("first-pixels"));
        Assert.True(SpinWait.SpinUntil(() => terminal.Contains("first-pixels"), TimeSpan.FromSeconds(5)));
        session.Append(image("second-pixels"));
        terminal.Keys.Enqueue(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
        Assert.True(SpinWait.SpinUntil(() => terminal.Contains("second-pixels"), TimeSpan.FromSeconds(5)));
        Assert.False(terminal.Contains("▀▀▀"));
        terminal.Keys.Enqueue(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false));
        session.Complete();
        Assert.Equal(1, terminal.Ends);
    }

    [Fact]
    public void Session_CancellationRestoresTerminalAndReleasesHistory() {
        var terminal = new ControlledTerminal();
        using var cancellation = new CancellationTokenSource();
        using var session = new StreamingPager(cancellation.Token, terminal);
        session.Append("alpha");
        Assert.True(SpinWait.SpinUntil(() => terminal.Contains("alpha"), TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        session.Complete();
        Assert.Equal(1, terminal.Ends);
        Assert.Throws<OperationCanceledException>(() => session.Append("late"));
    }

    private sealed class ControlledTerminal : IPagerTerminal {
        public TerminalGraphics Graphics { get; set; }
        private readonly Lock _gate = new();
        private readonly StringBuilder _output = new();
        internal readonly ConcurrentQueue<ConsoleKeyInfo> Keys = new();
        internal int Ends;
        internal bool FailWrites;
        public int Width => 40;
        public int Height => 8;
        internal bool Contains(string text) {
            lock (_gate)
                return _output.ToString().Contains(text, StringComparison.Ordinal);
        }
        public void Begin() {

        }
        public void End() => Interlocked.Increment(ref Ends);
        public bool TryReadKey(out ConsoleKeyInfo key) => Keys.TryDequeue(out key);
        public void Write(ReadOnlySpan<char> text) {
            if (FailWrites)
                throw new IOException("Simulated terminal failure.");
            lock (_gate)
                _output.Append(text);
        }
        public void Flush() {

        }
        public void Idle() => Thread.Yield();
    }
}
