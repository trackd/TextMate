namespace PSTextMate.Terminal;

internal sealed record PagerDocumentEntry(
    int RenderableIndex,
    IRenderable Renderable,
    Func<string> GetSearchText,
    Func<int[]> GetLineStarts,
    bool IsImage
) {
    public string SearchText => GetSearchText();

    public int[] LineStarts => GetLineStarts();
}

internal sealed partial class PagerDocument {
    private readonly List<PagerDocumentEntry> _entries = [];
    private static readonly Regex s_hyperlinkTargetRegex = HyperlinkTargetRegex();

    public IReadOnlyList<PagerDocumentEntry> Entries => _entries;

    public IReadOnlyList<IRenderable> Renderables { get; private set; } = [];

    public PagerDocument(IEnumerable<IRenderable> renderables) {
        Initialize(renderables, sourceLines: null);
    }

    private PagerDocument(IEnumerable<IRenderable> renderables, IReadOnlyList<string>? sourceLines) {
        Initialize(renderables, sourceLines);
    }

    private void Initialize(IEnumerable<IRenderable> renderables, IReadOnlyList<string>? sourceLines) {
        ArgumentNullException.ThrowIfNull(renderables);

        var renderableList = new List<IRenderable>();
        int index = 0;
        foreach (IRenderable renderable in renderables) {
            int entryIndex = index;
            bool isImage = IsImageRenderable(renderable);
            Lazy<string> lazySearchText = new(
                () => isImage
                    ? string.Empty
                    : sourceLines is not null
                    ? Normalize(sourceLines[entryIndex])
                    : ExtractSearchText(renderable),
                isThreadSafe: false
            );
            Lazy<int[]> lazyLineStarts = new(
                () => BuildLineStarts(lazySearchText.Value),
                isThreadSafe: false
            );

            _entries.Add(new PagerDocumentEntry(
                index,
                renderable,
                () => lazySearchText.Value,
                () => lazyLineStarts.Value,
                isImage
            ));
            renderableList.Add(renderable);
            index++;
        }

        Renderables = renderableList;
    }

    public static PagerDocument FromHighlightedText(HighlightedText highlightedText) {
        ArgumentNullException.ThrowIfNull(highlightedText);

        IReadOnlyList<string>? sourceLines = highlightedText.SourceLines;
        return sourceLines is not null && sourceLines.Count == highlightedText.Renderables.Length
            ? new PagerDocument(highlightedText.Renderables, sourceLines)
            : new PagerDocument(highlightedText.Renderables);
    }

    public PagerDocumentEntry? GetEntry(int renderableIndex) {
        return renderableIndex < 0 || renderableIndex >= _entries.Count
            ? null
            : _entries[renderableIndex];
    }

    private static string ExtractSearchText(IRenderable renderable) {
        try {
            string rendered = Writer.WriteToString(renderable, width: 200);
            string visibleText = Normalize(VTHelpers.StripAnsi(rendered));
            string hyperlinkTargets = ExtractHyperlinkTargets(rendered);

            return !string.IsNullOrEmpty(hyperlinkTargets)
                ? string.IsNullOrEmpty(visibleText)
                    ? hyperlinkTargets
                    : $"{visibleText}\n{hyperlinkTargets}"
                : !string.IsNullOrEmpty(visibleText)
                ? visibleText
                : Normalize(renderable.ToString());
        }
        catch (InvalidOperationException) {
            return Normalize(renderable.ToString());
        }
        catch (IOException) {
            return Normalize(renderable.ToString());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) {
            return Normalize(renderable.ToString());
        }
    }

    private static string ExtractHyperlinkTargets(string rendered) {
        if (string.IsNullOrEmpty(rendered)) {
            return string.Empty;
        }

        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in s_hyperlinkTargetRegex.Matches(rendered)) {
            string url = match.Groups["url"].Value;
            if (string.IsNullOrWhiteSpace(url)) {
                continue;
            }

            if (seen.Add(url)) {
                urls.Add(url);
            }
        }

        return urls.Count == 0
            ? string.Empty
            : Normalize(string.Join('\n', urls));
    }

    [GeneratedRegex("\\x1b\\]8;;(?<url>.*?)(?:\\x1b\\\\|\\x07)", RegexOptions.NonBacktracking, 250)]
    private static partial Regex HyperlinkTargetRegex();

    private static string Normalize(string? value) {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static int[] BuildLineStarts(string value) {
        if (string.IsNullOrEmpty(value)) {
            return [0];
        }

        var starts = new List<int> { 0 };
        for (int i = 0; i < value.Length; i++) {
            if (value[i] == '\n' && i + 1 < value.Length) {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    private static bool IsImageRenderable(IRenderable renderable) {
        string name = renderable.GetType().Name;
        return name.Contains("Sixel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pixel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Image", StringComparison.OrdinalIgnoreCase);
    }
}
