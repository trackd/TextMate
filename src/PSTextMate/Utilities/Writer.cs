namespace PSTextMate.Utilities;

/// <summary>
/// High-throughput Spectre.Console string renderer facade.
/// Uses a cached in-memory Spectre console and returns rendered strings.
/// </summary>
public static class Writer {
    private static readonly StringWriter StringConsoleWriter = new();
    private static readonly IAnsiConsole StringConsole = CreateStringConsole(StringConsoleWriter);
    private static readonly object SyncRoot = new();

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
    public static string Write(HighlightedText highlightedText, bool autoPage = true) {
        ArgumentNullException.ThrowIfNull(highlightedText);

        if (highlightedText.Page || (autoPage && ShouldPage(highlightedText))) {
            using var pager = new Pager(highlightedText);
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
    public static string WriteToString(IRenderable renderable, int? width = null) {
        ArgumentNullException.ThrowIfNull(renderable);

        lock (SyncRoot) {
            StringConsole.Profile.Width = ResolveWidth(width);

            StringConsole.Write(renderable);
            string output = StringConsoleWriter.ToString().TrimEnd();
            StringConsoleWriter.GetStringBuilder().Clear();
            return output;
        }
    }

    /// <summary>
    /// Compatibility wrapper for previous API shape.
    /// No host-direct output is performed; this returns the rendered string only.
    /// </summary>
    public static string WriteToStringWithHostFallback(IRenderable renderable, int? width = null)
        => WriteToString(renderable, width);

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
