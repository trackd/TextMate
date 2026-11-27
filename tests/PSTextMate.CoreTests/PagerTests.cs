using System.Text;
using System.Text.RegularExpressions;
using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed partial class PagerTests {
    [GeneratedRegex("\u001b\\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex TerminalSequence();
    private static PagerState Create(string? source = null, int width = 40, int height = 6) {
        source ??= string.Join('\n', Enumerable.Range(1, 100).Select(index => $"line {index:000}"));
        var state = new PagerState(new TextMateView(SyntaxHighlighter.Highlight(source, "plaintext")));
        state.Resize(width, height);
        return state;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char character = '\0', bool shift = false, bool control = false) => new(character, key, shift, false, control);

    [Fact]
    public void Navigation_RestoresOriginalBindingsAndClampsAtBothEnds() {
        PagerState state = Create();
        state.Handle(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, state.Top);
        state.Handle(Key(ConsoleKey.PageDown));
        Assert.Equal(6, state.Top);
        state.Handle(Key(ConsoleKey.H, 'h'));
        Assert.Equal(1, state.Top);
        state.Handle(Key(ConsoleKey.Home));
        state.Handle(Key(ConsoleKey.UpArrow));
        Assert.Equal(0, state.Top);
        state.Handle(Key(ConsoleKey.End));
        Assert.Equal(95, state.Top);
        state.Handle(Key(ConsoleKey.PageDown));
        Assert.Equal(95, state.Top);
        state.Handle(Key(ConsoleKey.G, 'g'));
        Assert.Equal(0, state.Top);
        state.Handle(Key(ConsoleKey.G, 'G', shift: true));
        Assert.Equal(95, state.Top);
        state.Handle(Key(ConsoleKey.U, control: true));
        Assert.Equal(93, state.Top);
    }

    [Fact]
    public void Search_IndexesEveryOccurrenceAndWrapsInBothDirections() {
        PagerState state = Create("alpha ALPHA\nbeta\nalpha\nlast", height: 2);
        state.SetQuery("alpha");
        Assert.Equal(3, state.Hits.Count);
        Assert.Equal(0, state.SelectedHit);
        state.Handle(Key(ConsoleKey.N, 'n'));
        Assert.Equal(1, state.SelectedHit);
        state.Handle(Key(ConsoleKey.N, 'n'));
        Assert.Equal(2, state.SelectedHit);
        Assert.Equal(2, state.Top);
        state.Handle(Key(ConsoleKey.N, 'n'));
        Assert.Equal(0, state.SelectedHit);
        state.Handle(Key(ConsoleKey.N, 'N', shift: true));
        Assert.Equal(2, state.SelectedHit);
        state.Handle(Key(ConsoleKey.C, 'c'));
        Assert.Empty(state.Hits);
        Assert.Empty(state.Query);
        Assert.Equal(-1, state.SelectedHit);
    }

    [Fact]
    public void Search_MissesAndWhitespaceRemainLiteralWithoutStaleHits() {
        PagerState state = Create("a b\na  b\nother");
        state.SetQuery(" ");
        Assert.Equal(3, state.Hits.Count);
        state.SetQuery("missing");
        Assert.Empty(state.Hits);
        Assert.Equal(-1, state.SelectedHit);
        state.Handle(Key(ConsoleKey.N, 'N', shift: true));
        Assert.Equal(0, state.Top);
    }

    [Fact]
    public void SearchEditor_SupportsInsertionDeletionAndCancelWithoutChangingActiveQuery() {
        PagerState state = Create("alpha\nbeta");
        state.SetQuery("alpha");
        state.Handle(Key(ConsoleKey.F, control: true));
        Assert.True(state.Editing);
        state.Handle(Key(ConsoleKey.Home));
        state.Handle(Key(ConsoleKey.Delete));
        state.Handle(Key(ConsoleKey.A, 'A'));
        Assert.Equal("Alpha", state.Input.ToString());
        state.Handle(Key(ConsoleKey.Escape));
        Assert.False(state.Editing);
        Assert.Equal("alpha", state.Query);
        state.Handle(Key(ConsoleKey.Oem2, '/'));
        state.Handle(Key(ConsoleKey.U, control: true));
        foreach (char value in "beta")
            state.Handle(Key(ConsoleKey.A, value));
        state.Handle(Key(ConsoleKey.Enter));
        Assert.Equal("beta", state.Query);
        Assert.Single(state.Hits);
    }

    [Fact]
    public void SearchEditor_DeletesWholeGraphemesAndBoundsInput() {
        PagerState state = Create();
        state.Handle(Key(ConsoleKey.Oem2, '/'));
        foreach (char value in "🦊e\u0301")
            state.Handle(Key(ConsoleKey.A, value));
        state.Handle(Key(ConsoleKey.Backspace));
        Assert.Equal("🦊", state.Input.ToString());
        state.Handle(Key(ConsoleKey.Backspace));
        Assert.True(state.Input.IsEmpty);
        for (int index = 0; index < 300; index++)
            state.Handle(Key(ConsoleKey.A, 'a'));
        Assert.Equal(256, state.Input.Length);
    }

    [Fact]
    public void Resize_ReflowsFromSourceWithoutRetokenizingAndRetainsVisibleAnchor() {
        HighlightedDocument lexical = SyntaxHighlighter.Highlight(new string('x', 100) + "\nanchor unique\nlast", "plaintext");
        var state = new PagerState(new TextMateView(lexical));
        state.Resize(40, 2);
        state.Handle(Key(ConsoleKey.End));
        state.Handle(Key(ConsoleKey.UpArrow));
        Assert.Equal("anchor unique", Text(state));
        state.Resize(80, 3);
        Assert.Equal("anchor unique", Text(state));
        int version = state.Version;
        HighlightedDocument document = state.Document;
        state.Resize(80, 3);
        Assert.Equal(version, state.Version);
        Assert.Same(document, state.Document);
        Assert.Same(lexical.Source, state.Document.Source);
    }

    [Fact]
    public void Frame_HighlightsMatchesPreservesUnicodeAndEmitsNoUnchangedRows() {
        PagerState state = Create("alpha 🦊\nsecond", width: 30);
        state.SetQuery("alpha");
        var terminal = new FakeTerminal();
        using var frame = new PagerFrame();
        frame.Draw(state, terminal);
        Assert.StartsWith("\u001b[?2026h", terminal.Output.ToString());
        Assert.EndsWith("\u001b[?2026l", terminal.Output.ToString());
        Assert.Contains("48;2;255;224;138", terminal.Output.ToString());
        Assert.Contains("🦊", terminal.Output.ToString());
        Assert.DoesNotContain("\u001b[2J", terminal.Output.ToString());
        int length = terminal.Output.Length;
        frame.Draw(state, terminal);
        Assert.Equal(length, terminal.Output.Length);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(8, 3)]
    public void Frame_TinyTerminalsAndWideInputRemainUsable(int width, int height) {
        PagerState state = Create("🦊\n\nend", width, height);
        state.Handle(Key(ConsoleKey.Oem2, '/'));
        foreach (char value in "🦊🦊")
            state.Handle(Key(ConsoleKey.A, value));
        var terminal = new FakeTerminal { Width = width, Height = height };
        using var frame = new PagerFrame();
        frame.Draw(state, terminal);
        Assert.True(terminal.Flushes > 0);
        state.Handle(Key(ConsoleKey.Escape));
        Assert.False(state.Editing);
    }

    [Fact]
    public void Frame_EndsSynchronizedOutputWhenAFrameWriteFails() {
        var terminal = new FakeTerminal { FailWriteAt = 2 };
        using var frame = new PagerFrame();
        Assert.Throws<IOException>(() => frame.Draw(Create(), terminal));
        Assert.Equal("\u001b[?2026h\u001b[?2026l", terminal.Output.ToString());
        Assert.Equal(1, terminal.Flushes);
    }

    [Fact]
    public void TerminalLoop_RespondsToIdleResizeWithoutRedrawingOnIdleAndRestoresSession() {
        PagerState state = Create("first\nsecond");
        var terminal = new FakeTerminal();
        terminal.Keys.Enqueue(null);
        terminal.Keys.Enqueue(null);
        terminal.Keys.Enqueue(Key(ConsoleKey.Q, 'q'));
        terminal.OnIdle = () => {
            if (terminal.Idles == 2)
                terminal.Width = 18;
        };
        TerminalPager.Run(state, terminal);
        Assert.Equal(1, terminal.Begins);
        Assert.Equal(1, terminal.Ends);
        Assert.Equal(2, terminal.Idles);
        Assert.Equal(17, state.Width);
        Assert.True(terminal.Flushes <= 2);
    }

    [Fact]
    public void TerminalLoop_CleansUpWhenRenderingFailsOrCancellationArrives() {
        var failed = new FakeTerminal { FailWrite = true };
        Assert.Throws<IOException>(() => TerminalPager.Run(Create(), failed));
        Assert.Equal(1, failed.Ends);
        using var cancellation = new CancellationTokenSource();
        var terminal = new FakeTerminal { OnIdle = cancellation.Cancel };
        TerminalPager.Run(Create(), terminal, cancellation.Token);
        Assert.Equal(1, terminal.Ends);
        var alreadyCancelled = new FakeTerminal();
        TerminalPager.Run(Create(), alreadyCancelled, cancellation.Token);
        Assert.Equal(0, alreadyCancelled.Begins);
    }

    [Fact]
    public void HelpAndControlC_WorkWithoutCommittingSearchInput() {
        PagerState state = Create();
        state.Handle(Key(ConsoleKey.Oem2, '?', shift: true));
        Assert.True(state.Help);
        state.Handle(Key(ConsoleKey.Escape));
        Assert.False(state.Help);
        Assert.False(state.ExitRequested);
        state.Handle(Key(ConsoleKey.Oem2, '/'));
        state.Handle(Key(ConsoleKey.C, control: true));
        Assert.True(state.ExitRequested);
        Assert.Empty(state.Query);
    }

    private static string Text(PagerState state) {
        HighlightedLine line = state.Document.Lines.Span[state.Top];
        return state.Document.Text.Substring(line.Start, line.Length);
    }

    [Fact]
    public void CachedRows_KeepTheSameScreenAsFreshRenderingAcrossEvictionSearchHelpAndResize() {
        PagerState state = Create(width: 100, height: 12);
        var terminal = new FakeTerminal { Width = 100, Height = 12 };
        using var frame = new PagerFrame();
        string[] screen = new string[12];
        for (int step = 0; step < 180; step++) {
            if (step == 60)
                state.SetQuery("line");
            if (step == 100)
                state.Handle(Key(ConsoleKey.Oem2, '?'));
            if (step == 101)
                state.Handle(Key(ConsoleKey.Escape));
            if (step == 120) {
                terminal.Width = 80;
                terminal.Height = 10;
                state.Resize(80, 10);
                screen = new string[10];
            }
            if (step is < 60 or >= 121)
                state.Handle(Key(step % 2 == 0 ? ConsoleKey.PageDown : ConsoleKey.DownArrow));
            else if (step is > 60 and < 100)
                state.Handle(Key(ConsoleKey.N, 'n'));
            terminal.Output.Clear();
            frame.Draw(state, terminal);
            ApplyRows(terminal.Output.ToString(), screen);
            using var fresh = new PagerFrame();
            var expectedTerminal = new FakeTerminal { Width = terminal.Width, Height = terminal.Height };
            fresh.Draw(state, expectedTerminal);
            string[] expected = new string[screen.Length];
            ApplyRows(expectedTerminal.Output.ToString(), expected);
            Assert.Equal(expected, screen);
        }
    }

    [GeneratedRegex("\u001b\\[(?<row>[0-9]+);1H\u001b\\[0m\u001b\\[2K")]
    private static partial Regex RowPosition();

    private static void ApplyRows(string output, string[] rows) {
        MatchCollection matches = RowPosition().Matches(output);
        for (int index = 0; index < matches.Count; index++) {
            Match match = matches[index];
            int start = match.Index + match.Length;
            int end = index + 1 < matches.Count ? matches[index + 1].Index : output.Length;
            string text = output[start..end];
            if (text.EndsWith("\u001b[?2026l", StringComparison.Ordinal))
                text = text[..^8];
            rows[int.Parse(match.Groups["row"].Value, System.Globalization.CultureInfo.InvariantCulture) - 1] = text;
        }
    }

    [Fact]
    public void UnchangedViewport_UsesCachedLayoutWithoutAllocating() {
        PagerState state = Create();
        var view = new TextMateView(state.Document);
        view.Layout(39);
        for (int index = 0; index < 100; index++) {
            state.Resize(40, 6);
            view.Layout(39);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++) {
            state.Resize(40, 6);
            view.Layout(39);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void View_AutoWidthCanGrowWhileExplicitWidthRemainsConstrained() {
        HighlightedDocument document = SyntaxHighlighter.Highlight(new string('x', 100), "plaintext");
        var automatic = new TextMateView(document, new RendererOptions(Width: 40, ConstrainWidth: false));
        var fixedWidth = new TextMateView(document, new RendererOptions(Width: 40, ConstrainWidth: true));
        Assert.Equal(3, automatic.Layout(40).LineCount);
        Assert.Equal(1, automatic.Layout(120).LineCount);
        Assert.Equal(3, fixedWidth.Layout(120).LineCount);
    }

    [Fact]
    public void SearchPanel_IsAtTopAndProgressUsesTerminalPalette() {
        PagerState state = Create(width: 100, height: 24);
        state.Handle(Key(ConsoleKey.Oem2, '/'));
        var terminal = new FakeTerminal { Width = 100, Height = 24 };
        using var frame = new PagerFrame();
        frame.Draw(state, terminal);
        using var expected = new TextBufferWriter();
        VT.WriteRow(expected, 1, PanelFrame.Border(state.Width, "Search"));
        Assert.Contains(expected.ToString(), terminal.Output.ToString());
        Assert.Contains("\u001b[92m", terminal.Output.ToString());
        Assert.DoesNotContain("\u001b[38;2;0;255;0m", terminal.Output.ToString());
        string output = terminal.Output.ToString();
        int body = output.IndexOf("\u001b[4;1H", StringComparison.Ordinal);
        int next = output.IndexOf("\u001b[5;1H", StringComparison.Ordinal);
        Assert.True(body >= 0 && next > body);
        Assert.StartsWith("line 001", VT.Parse(output[body..next]).Text);
    }

    [Fact]
    public void RichFooterAndSearchPanel_KeepRoundedBordersInsideViewportWithWideInput() {
        PagerState state = Create(width: 100, height: 24);
        state.Handle(Key(ConsoleKey.Oem2, '/'));
        foreach (char value in new string('界', 100))
            state.Handle(Key(ConsoleKey.A, value));
        state.Handle(Key(ConsoleKey.Home));
        var terminal = new FakeTerminal { Width = 100, Height = 24 };
        using var frame = new PagerFrame();
        frame.Draw(state, terminal);
        string plain = TerminalSequence().Replace(terminal.Output.ToString(), match => match.Value.EndsWith('H') ? "\n" : string.Empty);
        string[] panelRows = [.. plain.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(row => row.StartsWith('│') || row.StartsWith('╭') || row.StartsWith('╰'))];
        Assert.Equal(6, panelRows.Length);
        Assert.All(panelRows, row => Assert.Equal(99, TerminalCells.Measure(row)));
        Assert.Contains("Search", panelRows[0]);
        Assert.StartsWith("╭", panelRows[3]);
        Assert.Contains("1-", panelRows[4]);
        Assert.EndsWith("╯", panelRows[5]);
    }

    [Fact]
    public void SixelImages_DrawWholeAndSkipTheirInteriorWhenScrolling() {
        string payload = VT.SixelStart + "\"1;1;6;8#1;2;100;0;0!6~" + VT.StringTerminator;
        var builder = new PresentedTextBuilder();
        builder.Append("head\n", TextStyle.Plain);
        builder.AddImage(builder.Length, new TerminalImage(3, 2, payload.AsMemory()));
        builder.Append("▀▀▀\n▀▀▀\nafter\n1\n2\n3\n4\n5\n6", TextStyle.Plain);
        HighlightedDocument document = builder.Build(SyntaxHighlighter.PlainText(string.Empty));
        Assert.Equal(new TextBounds(0, 1, 3, 2), document.GetImageBounds(0));
        var state = new PagerState(new TextMateView(document)) { KeepImagesWhole = true };
        state.Resize(12, 6);
        var terminal = new FakeTerminal { Width = 12, Height = 6, Graphics = new(true, 2, 4) };
        using var frame = new PagerFrame();
        frame.Draw(state, terminal);
        Assert.Contains(payload, terminal.Output.ToString());
        state.Handle(Key(ConsoleKey.DownArrow));
        state.Handle(Key(ConsoleKey.DownArrow));
        Assert.Equal(3, state.Top);
        terminal.Output.Clear();
        frame.Draw(state, terminal);
        Assert.DoesNotContain(VT.SixelStart, terminal.Output.ToString());
        Assert.DoesNotContain("▀▀▀", VT.Parse(terminal.Output.ToString()).Text);
        state.Handle(Key(ConsoleKey.UpArrow));
        Assert.Equal(1, state.Top);
        terminal.Output.Clear();
        frame.Draw(state, terminal);
        Assert.Contains(payload, terminal.Output.ToString());
        HighlightedDocument composed = document.AsContent().Panel("image").Layout(16);
        Assert.Single(composed.Images.ToArray());
        Assert.Equal(2, composed.GetImageBounds(0).Column);
    }

    [Fact]
    public void WholeImageScrolling_KeepsTheFinalRowsReachableWithoutEnteringAnImage() {
        var builder = new PresentedTextBuilder();
        builder.Append("head\n", TextStyle.Plain);
        builder.AddImage(builder.Length, new TerminalImage(1, 5, VT.SixelStart.AsMemory()));
        builder.Append("▀\n▀\n▀\n▀\n▀\na\nb\nc\nd\ne\nf", TextStyle.Plain);
        HighlightedDocument document = builder.Build(SyntaxHighlighter.PlainText(string.Empty));
        var state = new PagerState(new TextMateView(document)) { KeepImagesWhole = true };
        state.Resize(20, 11);
        state.Handle(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, state.Top);
        state.Handle(Key(ConsoleKey.DownArrow));
        Assert.Equal(6, state.Top);
        state.Handle(Key(ConsoleKey.DownArrow));
        Assert.Equal(7, state.Top);
        state.Handle(Key(ConsoleKey.UpArrow));
        Assert.Equal(6, state.Top);
        state.Handle(Key(ConsoleKey.UpArrow));
        Assert.Equal(1, state.Top);
        state.Handle(Key(ConsoleKey.End));
        Assert.Equal(6, state.Top);
        Assert.True(state.Top + state.ContentRows >= state.LineCount);
    }

    [Fact]
    public void SixelImages_SuppressAllSideBySideCellFallbacks() {
        string payload = VT.SixelStart + "~" + VT.StringTerminator;
        var builder = new PresentedTextBuilder();
        builder.AddImage(0, new TerminalImage(1, 2, payload.AsMemory()));
        builder.AddImage(3, new TerminalImage(1, 2, payload.AsMemory()));
        builder.Append("▀  ▀\n▀  ▀\ntail", TextStyle.Plain);
        var state = new PagerState(new TextMateView(builder.Build(SyntaxHighlighter.PlainText(string.Empty)))) {
            KeepImagesWhole = true
        };
        state.Resize(12, 6);
        var terminal = new FakeTerminal { Width = 12, Height = 6, Graphics = new(true, 2, 4) };
        using var frame = new PagerFrame();
        frame.Draw(state, terminal);
        Assert.DoesNotContain('▀', terminal.Output.ToString());
        Assert.Equal(2, terminal.Output.ToString().Split(payload, StringSplitOptions.None).Length - 1);
    }

    private sealed class FakeTerminal : IPagerTerminal {
        public TerminalGraphics Graphics { get; set; }
        public int Width { get; set; } = 30;
        public int Height { get; set; } = 6;
        internal Queue<ConsoleKeyInfo?> Keys { get; } = new();
        internal StringBuilder Output { get; } = new();
        internal int Begins;
        internal int Ends;
        internal int Flushes;
        internal int Idles;
        internal bool FailWrite;
        internal int FailWriteAt;
        private int _writes;
        internal Action? OnIdle;
        public void Begin() => Begins++;
        public void End() => Ends++;
        public void Write(ReadOnlySpan<char> text) {
            if (FailWrite || ++_writes == FailWriteAt)
                throw new IOException("Simulated terminal failure");
            Output.Append(text);
        }
        public void Flush() => Flushes++;
        public void Idle() {
            Idles++;
            OnIdle?.Invoke();
        }
        public bool TryReadKey(out ConsoleKeyInfo key) {
            ConsoleKeyInfo? value = Keys.Count > 0 ? Keys.Dequeue() : null;
            key = value ?? default;
            return value.HasValue;
        }
    }
}
