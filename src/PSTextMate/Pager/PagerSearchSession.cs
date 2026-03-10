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
        if (_hits.Count == 0) {
            return s_noHits;
        }

        List<PagerSearchHit>? matches = null;
        foreach (PagerSearchHit hit in _hits) {
            if (hit.RenderableIndex != renderableIndex) {
                continue;
            }

            matches ??= [];
            matches.Add(hit);
        }

        return matches ?? s_noHits;
    }

    public bool IsCurrentHit(PagerSearchHit hit)
        => CurrentHit is PagerSearchHit current
            && current.RenderableIndex == hit.RenderableIndex
            && current.Offset == hit.Offset
            && current.Length == hit.Length;

    private void RebuildHits() {
        _hits.Clear();
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
                _hits.Add(new PagerSearchHit(entry.RenderableIndex, hitOffset, Query.Length, line, column));

                searchStart = hitOffset + Math.Max(1, Query.Length);
            }
        }
    }

    private static (int line, int column) ResolveLineColumn(int[] lineStarts, int offset) {
        if (lineStarts.Length == 0) {
            return (0, offset);
        }

        int line = 0;
        for (int i = 1; i < lineStarts.Length; i++) {
            if (lineStarts[i] > offset) {
                break;
            }

            line = i;
        }

        int column = offset - lineStarts[line];
        return (line, Math.Max(0, column));
    }
}
