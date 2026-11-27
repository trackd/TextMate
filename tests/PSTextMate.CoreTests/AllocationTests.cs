using System.Runtime.CompilerServices;
using System.Text;
using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class AllocationTests {
    [Fact]
    public void PreparedAndUnsafeSourceWrites_AllocateNothingWithReusableSink() {
        HighlightedDocument source = SyntaxHighlighter.Highlight("Write-Output 'hello'\n\u001b[2Jliteral", lineNumbers: true);
        HighlightedDocument prepared = DocumentPresentation.Prepare(source, new RendererOptions(Width: 80));
        using var sink = new CountingSink();
        void write() {
            source.WriteTo(sink);
            prepared.WriteTo(sink);
        }
        Assert.Equal(0, Measure(write));
        Assert.True(sink.Characters > 0);
        Assert.Contains('␛', source.ToAnsi());
        Assert.Contains('\u001b', source.Source);
    }

    [Fact]
    public void RepeatedComposedOutput_WritesSpansWithoutRebuildingLayout() {
        var content = TextContent.Columns(2, new TextColumn(TextContent.Text("first").Panel("Left")), new TextColumn(TextContent.Progress(3, 4).Panel("Right")));
        using var sink = new CountingSink();
        Assert.Equal(0, Measure(() => content.WriteTo(sink, 80)));
        Assert.Same(content.Layout(80), content.Layout(80));
        Assert.True(sink.Characters > 0);
    }

    [Fact]
    public void PlainSourceStreaming_AllocatesNothingWithoutCreatingADocument() {
        const string source = "raw\u001b[2J text\r\nwith\ttabs\nlast";
        using var sink = new CountingSink();
        Assert.Equal(0, Measure(() => SyntaxHighlighter.WriteTo(source.AsSpan(), sink, "plaintext", lineNumbers: true)));
        Assert.True(sink.Characters > 0);
    }

    [Theory]
    [InlineData("Idle")]
    [InlineData("Scroll")]
    [InlineData("Search")]
    [InlineData("Edit")]
    public void WarmPagerFrames_AllocateNothingDuringRepeatedInteraction(string interaction) {
        string source = string.Join('\n', Enumerable.Range(1, 100).Select(index => $"line {index:000} 🦊"));
        var state = new PagerState(new TextMateView(SyntaxHighlighter.Highlight(source, "plaintext", lineNumbers: true)));
        state.Resize(120, 30);
        if (interaction == "Search")
            state.SetQuery("line");
        if (interaction == "Edit")
            state.Handle(Key(ConsoleKey.Oem2, '/'));
        using var frame = new PagerFrame();
        var terminal = new CountingTerminal();
        bool alternate = false;
        void draw() {
            alternate = !alternate;
            if (interaction == "Scroll")
                state.Handle(Key(alternate ? ConsoleKey.DownArrow : ConsoleKey.UpArrow));
            if (interaction == "Search")
                state.Handle(Key(ConsoleKey.N, 'n'));
            if (interaction == "Edit")
                state.Handle(Key(alternate ? ConsoleKey.A : ConsoleKey.Backspace, alternate ? 'a' : '\b'));
            frame.Draw(state, terminal);
        }
        Assert.Equal(0, Measure(draw));
        Assert.True(terminal.Characters > 0);
    }

    [Theory]
    [InlineData("PowerShellMultiline")]
    [InlineData("CSharpMultiline")]
    [InlineData("PlainControls")]
    public void StreamingLexicalOutput_MatchesRetainedDocumentBytes(string scenario) {
        (string source, string language) = scenario switch {
            "PowerShellMultiline" => ("$value = @'\r\nhello 🦊\r\n'@\r\nWrite-Output\t$value\n", "powershell"),
            "CSharpMultiline" => ("/* multi\nline */\nvar value = \"hello\";\n\n", "csharp"),
            "PlainControls" => ("literal\u001b[2J\ttext\r\n\u0007end\n", "plaintext"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        HighlightedDocument document = SyntaxHighlighter.Highlight(source, language, lineNumbers: true);
        using var writer = new TextBufferWriter();
        SyntaxHighlighter.WriteTo(source, writer, language, lineNumbers: true);
        Assert.Equal(document.ToAnsi(), writer.ToString());
        writer.Clear();
        SyntaxHighlighter.WriteTo(source, writer, language, lineNumbers: true, ansi: false);
        using var expected = new StringWriter();
        document.WriteTo(expected, ansi: false);
        Assert.Equal(expected.ToString(), writer.ToString());
    }

    [Fact]
    public void ReusableBuffer_GrowsPreservesSnapshotsAndRejectsWritesAfterDisposal() {
        var writer = new TextBufferWriter(1);
        writer.Write("before");
        string snapshot = writer.ToString();
        writer.Write(new string('界', 1000));
        writer.Write(int.MinValue);
        writer.Write(long.MinValue);
        Assert.StartsWith(snapshot, writer.ToString());
        Assert.EndsWith("-2147483648-9223372036854775808", writer.ToString());
        Assert.Equal("before", snapshot);
        writer.Clear();
        "span".AsSpan().CopyTo(writer.GetSpan(4));
        writer.Advance(4);
        Assert.Equal("span", writer.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(writer.Capacity));
        writer.Clear();
        Assert.Empty(writer.ToString());
        writer.Dispose();
        writer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => writer.Write('x'));
    }

    [Fact]
    public void BufferedSpanSink_PreservesWriteOrderAndFlushesWithoutMaterializingStrings() {
        using var output = new StringWriter();
        using var buffer = new TextBufferWriter(16);
        buffer.Attach(output, 16);
        for (int index = 0; index < 50; index++)
            buffer.Write("abcd".AsSpan());
        buffer.Write('!');
        buffer.Flush();
        Assert.Equal(string.Concat(Enumerable.Repeat("abcd", 50)) + "!", output.ToString());
        Assert.Equal(0, buffer.Length);
        buffer.Detach();
        buffer.Write("kept");
        Assert.Equal("kept", buffer.ToString());
    }

    [Fact]
    public void StreamingFailureOrCancellation_DoesNotLeakOrPoisonTokenizerSession() {
        const string source = "Write-Output 'first'\nWrite-Output 'second'";
        using var failed = new FailingSink();
        Assert.Throws<IOException>(() => SyntaxHighlighter.WriteTo(source, failed));
        using var writer = new TextBufferWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SyntaxHighlighter.WriteTo(source, writer, cancellationToken: cancellation.Token));
        Assert.Equal(0, writer.Length);
        SyntaxHighlighter.WriteTo(source, writer);
        Assert.Equal(SyntaxHighlighter.Highlight(source).ToAnsi(), writer.ToString());
    }

    private static long Measure(Action operation) {
        for (int index = 0; index < 32; index++)
            RunBatch(operation);
        long before = GC.GetAllocatedBytesForCurrentThread();
        RunBatch(operation);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunBatch(Action operation) {
        for (int index = 0; index < 1000; index++)
            operation();
    }
    private static ConsoleKeyInfo Key(ConsoleKey key, char character = '\0') => new(character, key, false, false, false);

    private sealed class CountingSink : TextWriter {
        public long Characters;
        public override Encoding Encoding => Encoding.Unicode;
        public override void Write(char value) => Characters++;
        public override void Write(string? value) => Characters += value?.Length ?? 0;
        public override void Write(ReadOnlySpan<char> buffer) => Characters += buffer.Length;
    }
    private sealed class FailingSink : TextWriter {
        public override Encoding Encoding => Encoding.Unicode;
        public override void Write(char value) => throw new IOException("Expected failure");
        public override void Write(string? value) => throw new IOException("Expected failure");
        public override void Write(ReadOnlySpan<char> buffer) => throw new IOException("Expected failure");
    }
    private sealed class CountingTerminal : IPagerTerminal {
        public long Characters;
        public int Width => 120;
        public int Height => 30;
        public void Begin() {

        }
        public void End() {

        }
        public void Write(ReadOnlySpan<char> text) => Characters += text.Length;
        public void Flush() {

        }
        public bool TryReadKey(out ConsoleKeyInfo key) {
            key = default;
            return false;
        }
        public void Idle() {

        }
    }
}
