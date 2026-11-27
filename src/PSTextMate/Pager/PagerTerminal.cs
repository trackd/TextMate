namespace PSTextMate.Core;

/// <summary>Instance boundary for terminal lifecycle, input, resizing and batched writes.</summary>
internal interface IPagerTerminal {
    TerminalGraphics Graphics => default;
    int Width { get; }
    int Height { get; }
    void Begin();
    void End();
    bool TryReadKey(out ConsoleKeyInfo key);
    void Write(ReadOnlySpan<char> text);
    void Flush();
    void Idle();
}

internal sealed class PagerTerminal(TerminalGraphics graphics = default, bool captureControlC = true,
    Func<bool>? retainedImages = null) : IPagerTerminal {
    public TerminalGraphics Graphics => graphics;
    public bool SupportsImages => graphics.CanDraw || retainedImages?.Invoke() == true;
    internal static bool CanDraw(IPagerTerminal terminal) => terminal is PagerTerminal owned
        ? owned.SupportsImages : terminal.Graphics.CanDraw;
    private bool _controlC;
    private bool _begun;
    private bool _cursorVisible = true;
    private ConsoleModeLease? _modes;
    public int Width => Console.WindowWidth;
    public int Height => Console.WindowHeight;

    public void Begin() {
        _controlC = Console.TreatControlCAsInput;
        if (OperatingSystem.IsWindows())
            _cursorVisible = Console.CursorVisible;
        _modes = ConsoleModeLease.Enter();
        _begun = true;
        Console.TreatControlCAsInput = captureControlC;
        VT.EnterPager(Console.Out);
        Flush();
    }

    public void End() {
        if (!_begun)
            return;
        try {
            VT.LeavePager(Console.Out, _cursorVisible);
            Flush();
        }
        finally {
            try {
                Console.TreatControlCAsInput = _controlC;
            }
            finally {
                _modes?.Dispose();
                _modes = null;
                _begun = false;
            }
        }
    }

    public bool TryReadKey(out ConsoleKeyInfo key) {
        key = default;
        if (!Console.KeyAvailable)
            return false;
        key = Console.ReadKey(true);
        return true;
    }

    public void Write(ReadOnlySpan<char> text) => Console.Out.Write(text);
    public void Flush() => Console.Out.Flush();
    public void Idle() => Thread.Sleep(25);
}

internal static class TerminalPager {
    private static readonly SemaphoreSlim Sessions = new(1, 1);

    internal static void Run(PagerState state, IPagerTerminal terminal, CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested)
            return;
        Sessions.Wait(cancellationToken);
        try {
            RunOwned(state, terminal, cancellationToken);
        }
        finally {
            Sessions.Release();
        }
    }

    private static void RunOwned(PagerState state, IPagerTerminal terminal, CancellationToken cancellationToken) {
        using var frame = new PagerFrame();
        int rendered = -1;
        try {
            terminal.Begin();
            while (!state.ExitRequested && !cancellationToken.IsCancellationRequested) {
                state.KeepImagesWhole = PagerTerminal.CanDraw(terminal);
                state.Resize(terminal.Width, terminal.Height);
                state.RefreshSource();
                if (rendered != state.Version) {
                    frame.Draw(state, terminal);
                    rendered = state.Version;
                }
                if (terminal.TryReadKey(out ConsoleKeyInfo key)) {
                    state.Handle(key);
                    // Coalesce bursts of navigation/paste without starving resize or cancellation checks.
                    for (int pending = 0; pending < 31 && !state.ExitRequested && !cancellationToken.IsCancellationRequested; pending++) {
                        if (!terminal.TryReadKey(out key))
                            break;
                        state.Handle(key);
                    }
                }
                else {
                    terminal.Idle();
                }
            }
        }
        finally {
            terminal.End();
        }
    }
}
