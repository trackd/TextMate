using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

// Capabilities are process-wide; these tests must not run beside others that read them.
[CollectionDefinition(nameof(TerminalCapabilitiesTests), DisableParallelization = true)]
public sealed class TerminalCapabilitiesCollection;

[Collection(nameof(TerminalCapabilitiesTests))]
public sealed class TerminalCapabilitiesTests : IDisposable {
    public void Dispose() => TerminalCapabilities.Reset();

    private static Func<string, string?> Environment(params string[] pairs) {
        Dictionary<string, string> values = pairs.Select(static pair => pair.Split('=', 2)).ToDictionary(static pair => pair[0], static pair => pair[1]);
        return name => values.GetValueOrDefault(name);
    }

    [Fact]
    public void Probe_WritesOneFencedBatchAndReadsEveryReply() {
        var terminal = new ScriptedTerminal("\u001b_Gi=31;OK\u001b\\\u001bP>|kitty(0.39.1)\u001b\\\u001b[6;21;9t\u001b[4;420;720t\u001b[8;20;80t\u001b[?62;4;22c");
        TerminalCapabilities.Probe(terminal, (80, 20));
        Assert.Equal(TerminalQuery.Probe, terminal.Written.ToString());
        TerminalCapabilities capabilities = TerminalCapabilities.Current;
        Assert.Equal(("kitty", true, true, 9, 21, true), (capabilities.Terminal, capabilities.Kitty, capabilities.Sixel,
            capabilities.CellWidth, capabilities.CellHeight, capabilities.CellSizeMeasured));
        Assert.True(terminal.EnteredQueryMode);
    }

    [Fact]
    public void Probe_DerivesCellsFromWindowPixelsAndTreatsAMissingKittyReplyAsUnsupported() {
        TerminalQuery.Result result = TerminalQuery.Parse("\u001b[4;400;800t\u001b[8;20;80t\u001b[?62;22c");
        Assert.Equal((false, false, 10, 20), (result.Kitty, result.Sixel, result.CellWidth, result.CellHeight));
        result = TerminalQuery.Parse("\u001b_Gi=31;ENOTSUPPORTED:no\u001b\\\u001b[?62;4;90c");
        Assert.Equal((false, true, true), (result.Kitty, result.Sixel, result.GoodImage));
    }

    [Fact]
    public void Probe_KeepsEarlierEvidenceWhenTheTerminalDoesNotAnswer() {
        TerminalCapabilities.Probe(new ScriptedTerminal(string.Empty), (80, 20));
        Assert.False(TerminalCapabilities.Current.Probed);
    }

    [Fact]
    public void Probe_NeverConsumesPendingUserInputOrTypedKeys() {
        var pending = new ScriptedTerminal("x") { KeyPending = true };
        Assert.Equal(string.Empty, TerminalQuery.Read(pending));
        Assert.Equal(string.Empty, pending.Written.ToString());
        Assert.Equal(string.Empty, TerminalQuery.Read(new ScriptedTerminal("a\u001b[?62c")));
    }

    [Theory]
    [InlineData("kitty", false, true, false, "KITTY_WINDOW_ID=1")]
    [InlineData("ghostty", false, true, false, "TERM=xterm-ghostty")]
    [InlineData("windowsterminal", true, false, false, "WT_SESSION=abc")]
    [InlineData("iterm2", false, false, true, "TERM_PROGRAM=iTerm.app")]
    [InlineData("wezterm", true, true, true, "TERM_PROGRAM=WezTerm", "TERM_PROGRAM_VERSION=20250402-110000-abcd")]
    [InlineData("wezterm", false, false, true, "TERM_PROGRAM=WezTerm", "TERM_PROGRAM_VERSION=20240203-110809-5046fc22")]
    [InlineData("vscode", true, false, true, "TERM_PROGRAM=vscode", "TERM_PROGRAM_VERSION=1.105.0")]
    [InlineData("unknown", false, false, false, "TERM=xterm-256color")]
    public void Environment_DetectsTerminalsLikeTheSixelModule(string terminal, bool sixel, bool kitty, bool inline, params string[] variables) {
        TerminalCapabilities capabilities = TerminalCapabilities.FromEnvironment(Environment(variables));
        Assert.Equal((terminal, sixel, kitty, inline), (capabilities.Terminal, capabilities.Sixel, capabilities.Kitty, capabilities.InlineImage));
        Assert.False(capabilities.Probed);
    }

    [Fact]
    public void ProbeAnswers_OverrideTheEnvironmentAndTheReportedIdentityWins() {
        TerminalQuery.Result result = TerminalQuery.Parse("\u001b_Gi=31;OK\u001b\\\u001bP>|WezTerm 20250402\u001b\\\u001b[?62;4c");
        TerminalCapabilities capabilities = TerminalCapabilities.FromProbe(result, Environment("WT_SESSION=1"));
        Assert.Equal(("wezterm", true, true, true), (capabilities.Terminal, capabilities.Sixel, capabilities.Kitty, capabilities.InlineImage));
        Assert.False(capabilities.CellSizeMeasured);
        Assert.Equal((10, 20), (capabilities.CellWidth, capabilities.CellHeight));
    }

    [Fact]
    public void Set_OverridesDetectionUntilReset() {
        TerminalCapabilities.Set(new TerminalCapabilities("tmux", false, true, false, false, 8, 16, false));
        Assert.Equal(new TerminalGraphics(false, 8, 16, Kitty: true), TerminalCapabilities.Current.Graphics);
        Assert.True(TerminalCapabilities.Current.CellSizeMeasured);
        TerminalCapabilities.Reset();
        Assert.False(TerminalCapabilities.Current.Probed);
    }

    [Fact]
    public void KittyWriter_ChunksAQuietPlacementTheParserAccepts() {
        byte[] png = new byte[5000];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 40, 0, 0, 0, 20 }.CopyTo(png, 0);
        string payload = TerminalImageWriter.Kitty(png, 4, 1);
        Assert.StartsWith("\u001b_Ga=T,f=100,t=d,q=2,C=1,c=4,r=1,m=1;", payload);
        Assert.EndsWith("\u001b\\", payload);
        Assert.Contains("\u001b_Gq=2,m=0;", payload);
        Assert.True(TerminalImageParser.TryRead(payload, new(10, 20), out int consumed, out TerminalImage? image));
        Assert.Equal(payload.Length, consumed);
        Assert.Equal((4, 1), (image!.Width, image.Height));
        Assert.Equal(TerminalImageWriter.Format.Png, TerminalImageWriter.Detect(png));
        Assert.Equal(TerminalImageWriter.Format.Gif, TerminalImageWriter.Detect("GIF89a"u8));
    }

    private sealed class ScriptedTerminal(string replies) : ITerminalQueryTransport {
        private int _read;
        private bool _written;
        internal readonly System.Text.StringBuilder Written = new();
        internal bool EnteredQueryMode;
        internal bool KeyPending;
        public bool IsRedirected => false;
        public bool KeyAvailable => KeyPending || (_written && _read < replies.Length);
        public long ElapsedMilliseconds { get; private set; }
        public char Read() => replies[_read++];
        public void Write(string text) {
            Written.Append(text);
            _written = true;
        }
        public void Wait() => ElapsedMilliseconds += 100;
        public IDisposable? EnterQueryMode() {
            EnteredQueryMode = true;
            return null;
        }
    }
}
