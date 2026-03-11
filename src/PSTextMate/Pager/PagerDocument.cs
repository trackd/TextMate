namespace PSTextMate.Terminal;

internal sealed record PagerDocumentEntry(
    int RenderableIndex,
    IRenderable Renderable,
    string SearchText,
    int[] LineStarts,
    bool IsImage
);

internal sealed class PagerDocument {
    private readonly List<PagerDocumentEntry> _entries = [];

    public IReadOnlyList<PagerDocumentEntry> Entries => _entries;

    public IReadOnlyList<IRenderable> Renderables { get; }

    public PagerDocument(IEnumerable<IRenderable> renderables) {
        ArgumentNullException.ThrowIfNull(renderables);

        var renderableList = new List<IRenderable>();
        int index = 0;
        foreach (IRenderable renderable in renderables) {
            bool isImage = IsImageRenderable(renderable);
            string searchText = isImage ? string.Empty : ExtractSearchText(renderable);
            int[] lineStarts = BuildLineStarts(searchText);
            _entries.Add(new PagerDocumentEntry(index, renderable, searchText, lineStarts, isImage));
            renderableList.Add(renderable);
            index++;
        }

        Renderables = renderableList;
    }

    public static PagerDocument FromHighlightedText(HighlightedText highlightedText) {
        ArgumentNullException.ThrowIfNull(highlightedText);
        return new PagerDocument(highlightedText.Renderables);
    }

    public PagerDocumentEntry? GetEntry(int renderableIndex) {
        return renderableIndex < 0 || renderableIndex >= _entries.Count
            ? null
            : _entries[renderableIndex];
    }

    private static string ExtractSearchText(IRenderable renderable) {
        if (renderable is Text text) {
            return Normalize(text.ToString());
        }

        try {
            string rendered = Writer.WriteToString(renderable, width: 200);
            string normalized = Normalize(VTHelpers.StripAnsi(rendered));
            return !string.IsNullOrEmpty(normalized)
                ? normalized
                : Normalize(renderable.ToString());
        }
        catch {
            return Normalize(renderable.ToString());
        }
    }

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
