#if netstandard2_0
// just a reference implementation
using PSTextMate.Core;
using Spectre.Console;
using System.Text;
using Spectre.Console.Rendering;

namespace PSTextMate.Utilities;

public abstract class TMConsole : IAnsiConsole {
    public Profile Profile { get; }
    public IAnsiConsoleCursor Cursor { get; }
    public IAnsiConsoleInput Input { get; }
    public IExclusivityMode ExclusivityMode { get; }
    public RenderPipeline Pipeline { get; }

    public int Width { get; set; } = 80;
    public int Height { get; set; } = 25;

    public TMConsole() {
        var writer = new NoopWriter();
        var output = new SimpleOutput(writer, () => Width, () => Height);

        Profile = new Profile(output, Encoding.Unicode) {
            Capabilities =
            {
                ColorSystem = ColorSystem.TrueColor,
                Unicode = true,
                Ansi = false,
                Links = false,
                Legacy = false,
                Interactive = false,
                AlternateBuffer = false
            },
        };

        Cursor = new NoopConsoleCursor();
        Input = new NoopConsoleInput();
        ExclusivityMode = new ExclusivityMode();
        Pipeline = new RenderPipeline();
    }

    public abstract void Clear(bool home);
    public abstract void Write(IRenderable renderable);
}

public sealed class NoopWriter : TextWriter {
    public override Encoding Encoding { get; } = Encoding.Unicode;
}

public sealed class SimpleOutput : IAnsiConsoleOutput {
    private readonly Func<int> _width;
    private readonly Func<int> _height;

    public TextWriter Writer { get; }
    public bool IsTerminal { get; } = true;
    public int Width => _width();
    public int Height => _height();

    public SimpleOutput(NoopWriter writer, Func<int> width, Func<int> height) {
        _width = width ?? throw new ArgumentNullException(nameof(width));
        _height = height ?? throw new ArgumentNullException(nameof(height));

        Writer = writer;
    }

    public void SetEncoding(Encoding encoding) {
    }
}

internal sealed class NoopConsoleCursor : IAnsiConsoleCursor {
    public void Show(bool show) {
    }

    public void SetPosition(int column, int line) {
    }

    public void Move(CursorDirection direction, int steps) {
    }
}

internal sealed class NoopConsoleInput : IAnsiConsoleInput {
    public bool IsKeyAvailable() => false;

    public ConsoleKeyInfo? ReadKey(bool intercept) => null;

    public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
        => Task.FromResult<ConsoleKeyInfo?>(null);
}

internal sealed class ExclusivityMode : IExclusivityMode {
    public T Run<T>(Func<T> func) => func();

    public Task<T> RunAsync<T>(Func<Task<T>> func) => func();
}
#endif
