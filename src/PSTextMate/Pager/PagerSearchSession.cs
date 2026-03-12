namespace PSTextMate.Terminal;

internal sealed record PagerSearchHit(
    int RenderableIndex,
    int Offset,
    int Length,
    int Line,
    int Column
);

internal sealed class PagerSearchSession {
    private readonly PagerDocument _document;
    private readonly List<PagerSearchHit> _hits = [];
    private readonly Dictionary<int, List<PagerSearchHit>> _hitsByRenderable = [];
    private static readonly IReadOnlyList<PagerSearchHit> s_noHits = [];
    public string Query { get; private set; } = string.Empty;
    public int CurrentHitIndex { get; private set; } = -1;
    public int HitCount => _hits.Count;
    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public PagerSearchHit? CurrentHit
        => CurrentHitIndex >= 0 && CurrentHitIndex < _hits.Count
            ? _hits[CurrentHitIndex]
            : null;

    public PagerSearchSession(PagerDocument document) {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public void SetQuery(string query) {
        Query = query?.Trim() ?? string.Empty;
        RebuildHits();
    }

    public PagerSearchHit? MoveNext(int topIndex) {
        if (_hits.Count == 0) {
            CurrentHitIndex = -1;
            return null;
        }

        if (CurrentHitIndex >= 0) {
            CurrentHitIndex = (CurrentHitIndex + 1) % _hits.Count;
            return _hits[CurrentHitIndex];
        }

        int nearest = _hits.FindIndex(hit => hit.RenderableIndex >= topIndex);
        CurrentHitIndex = nearest >= 0 ? nearest : 0;
        return _hits[CurrentHitIndex];
    }

    public PagerSearchHit? MovePrevious(int topIndex) {
        if (_hits.Count == 0) {
            CurrentHitIndex = -1;
            return null;
        }

        if (CurrentHitIndex >= 0) {
            CurrentHitIndex = CurrentHitIndex == 0 ? _hits.Count - 1 : CurrentHitIndex - 1;
            return _hits[CurrentHitIndex];
        }

        int nearest = _hits.FindLastIndex(hit => hit.RenderableIndex <= topIndex);
        CurrentHitIndex = nearest >= 0 ? nearest : _hits.Count - 1;
        return _hits[CurrentHitIndex];
    }

    public IReadOnlyList<PagerSearchHit> GetHitsForRenderable(int renderableIndex) {
        return _hitsByRenderable.TryGetValue(renderableIndex, out List<PagerSearchHit>? matches)
            ? matches
            : s_noHits;
    }

    private void RebuildHits() {
        _hits.Clear();
        _hitsByRenderable.Clear();
        CurrentHitIndex = -1;

        if (!HasQuery) {
            return;
        }

        foreach (PagerDocumentEntry entry in _document.Entries) {
            if (entry.IsImage || string.IsNullOrEmpty(entry.SearchText)) {
                continue;
            }

            int searchStart = 0;
            while (searchStart <= entry.SearchText.Length - Query.Length) {
                int hitOffset = entry.SearchText.IndexOf(Query, searchStart, StringComparison.OrdinalIgnoreCase);
                if (hitOffset < 0) {
                    break;
                }

                (int line, int column) = ResolveLineColumn(entry.LineStarts, hitOffset);
                var hit = new PagerSearchHit(entry.RenderableIndex, hitOffset, Query.Length, line, column);
                _hits.Add(hit);

                if (!_hitsByRenderable.TryGetValue(entry.RenderableIndex, out List<PagerSearchHit>? existing)) {
                    _hitsByRenderable[entry.RenderableIndex] = [hit];
                }
                else {
                    existing.Add(hit);
                }

                searchStart = hitOffset + Math.Max(1, Query.Length);
            }
        }

    }

    private static (int line, int column) ResolveLineColumn(int[] lineStarts, int offset) {
        if (lineStarts.Length == 0) {
            return (0, offset);
        }

        int line = Array.BinarySearch(lineStarts, offset);
        line = line >= 0 ? line : Math.Max(0, (~line) - 1);

        int column = offset - lineStarts[line];
        return (line, Math.Max(0, column));
    }
}
