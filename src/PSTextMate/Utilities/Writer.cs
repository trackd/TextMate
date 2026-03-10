namespace PSTextMate.Utilities;

/// <summary>
/// High-throughput Spectre.Console string renderer facade.
/// Uses a cached in-memory Spectre console and returns rendered strings.
/// </summary>
public static class Writer {
    private sealed class RenderContext {
        public StringBuilder Buffer { get; }
        public StringWriter Writer { get; }
        public IAnsiConsole Console { get; }
        public RenderContext() {
            Buffer = new StringBuilder(2048);
            Writer = new StringWriter(Buffer, CultureInfo.InvariantCulture);
            Console = CreateStringConsole(Writer);
        }
    }

    [ThreadStatic]
    private static RenderContext? _threadContext;

    /// <summary>
    /// Renders a single renderable to string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Write(IRenderable renderable) {
        ArgumentNullException.ThrowIfNull(renderable);
        return WriteToString(renderable);
    }

    /// <summary>
    /// Renders highlighted text to string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Write(HighlightedText highlightedText, bool autoPage = false) {
        ArgumentNullException.ThrowIfNull(highlightedText);

        if (highlightedText.Page || (autoPage && ShouldPage(highlightedText))) {
            var pager = new Pager(highlightedText);
            pager.Show();
            return string.Empty;
        }

        // Sixel payload must be written as raw control sequences. Converting to a string
        // and flowing through host formatting can strip DCS wrappers and print payload text.
        if (ContainsImageRenderables(highlightedText.Renderables)) {
            AnsiConsole.Write(highlightedText);
            return string.Empty;
        }

        return WriteToString(highlightedText);
    }

    /// <summary>
    /// Renders a sequence of renderables as rows.
    /// </summary>
    public static string Write(IEnumerable<IRenderable> renderables) {
        ArgumentNullException.ThrowIfNull(renderables);

        return renderables is IRenderable[] array
            ? array.Length == 0 ? string.Empty : array.Length == 1 ? WriteToString(array[0]) : WriteToString(new Rows(array))
            : renderables is IReadOnlyList<IRenderable> list
            ? list.Count == 0 ? string.Empty : list.Count == 1 ? WriteToString(list[0]) : WriteToString(new Rows(list))
            : WriteToString(new Rows(renderables));
    }

    /// <summary>
    /// Renders a Spectre renderable to a reusable in-memory writer.
    /// Uses a stable in-memory rendering path so the output can be streamed
    /// as plain text, redirected, or post-processed by custom formatters.
    /// </summary>
    internal static string WriteToString(IRenderable renderable, int? width = null) {
        ArgumentNullException.ThrowIfNull(renderable);

        RenderContext context = _threadContext ??= new RenderContext();
        context.Console.Profile.Width = ResolveWidth(width);

        context.Console.Write(renderable);
        return GetTrimmedOutputAndReset(context.Buffer);
    }

    /// <summary>
    /// Compatibility wrapper for previous API shape.
    /// No host-direct output is performed; this returns the rendered string only.
    /// </summary>
    internal static string WriteToStringWithHostFallback(IRenderable renderable, int? width = null)
        => WriteToString(renderable, width);

    private static string GetTrimmedOutputAndReset(StringBuilder buffer) {
        int end = buffer.Length;
        while (end > 0 && char.IsWhiteSpace(buffer[end - 1])) {
            end--;
        }

        string output = end == 0 ? string.Empty : buffer.ToString(0, end);
        buffer.Clear();
        return output;
    }

    private static IAnsiConsole CreateStringConsole(StringWriter writer) {
        var settings = new AnsiConsoleSettings {
            Out = new AnsiConsoleOutput(writer)
        };

        return AnsiConsole.Create(settings);
    }

    private static int ResolveWidth(int? widthOverride) {
        int width = widthOverride ?? GetConsoleWidth();
        return Math.Max(1, width);
    }

    private static int GetConsoleWidth() {
        try {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        }
        catch {
            return 80;
        }
    }

    private static bool ContainsImageRenderables(IEnumerable<IRenderable> renderables)
        => renderables.Any(IsImageRenderable);

    private static bool ShouldPage(HighlightedText highlightedText) {
        int windowHeight = GetConsoleHeight();
        return highlightedText.LineCount > Math.Max(1, windowHeight - 2);
    }

    private static int GetConsoleHeight() {
        try {
            return Console.WindowHeight > 0 ? Console.WindowHeight : 40;
        }
        catch {
            return 40;
        }
    }

    private static bool IsImageRenderable(IRenderable renderable) {
        string name = renderable.GetType().Name;
        return name.Contains("Sixel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pixel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Image", StringComparison.OrdinalIgnoreCase);
    }
}
