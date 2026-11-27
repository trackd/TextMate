namespace PSTextMate.Core;

internal readonly record struct PagerHit(int Line, int Start, int Length);

/// <summary>Terminal-independent pager navigation, bounded editing and indexed literal search.</summary>
internal sealed class PagerState(ITextRenderable view) {
    private readonly IPagerSource? _source;
    private int _sourceVersion = -1;
    private int _indexedLines;
    internal bool HitsTruncated { get; private set; }
    internal PagerState(IPagerSource source) : this(new TextMateView(SyntaxHighlighter.PlainText(string.Empty))) {
        _source = source;
    }
    internal int LineCount => _source?.LineCount ?? Document.LineCount;
    internal bool IsStreaming => _source is not null && !_source.IsComplete;
    internal bool Follow { get; private set; }
    internal bool KeepImagesWhole { get; set; }
    internal int ImageCount => _source?.ImageCount ?? Document?.Images.Length ?? 0;
    internal TextBounds GetImageBounds(int index) => _source?.GetImageBounds(index) ?? Document.GetImageBounds(index);
    internal TerminalImage ReadImage(int index) => _source?.ReadImage(index) ?? Document.Images.Span[index].Image;
    private int _lastTop;
    internal PagerLine ReadLine(int index) => _source?.ReadLine(index) ?? new(Document, Document.Lines.Span[index], 0);
    internal void RefreshSource() {
        if (_source is null || _sourceVersion == _source.Version)
            return;
        _sourceVersion = _source.Version;
        LineNumberDigits = LineNumberGutter.Digits(Math.Max(1, LineCount));
        IndexSearch(_indexedLines);
        if (Follow)
            Top = LineCount;
        ClampTop();
        Version++;
    }
    private readonly List<PagerHit> _hits = [];
    private readonly char[] _input = new char[256];
    private int _inputLength;
    private int _width;

    internal HighlightedDocument Document { get; private set; } = null!;
    internal int Top { get; private set; }
    internal int Width => Math.Max(0, _width - 1);
    internal int Height { get; private set; }
    internal int LineNumberDigits { get; private set; } = 1;
    internal int FooterRows => Height >= 4 && Width >= 64 + (LineNumberDigits * 3) ? 3 : 1;
    internal int SearchRows => Editing && Height >= 7 ? 3 : 0;
    internal int ContentRows => Math.Max(0, Height - FooterRows - SearchRows);
    internal int Version { get; private set; }
    internal int SearchVersion { get; private set; }
    internal bool ExitRequested { get; private set; }
    internal bool Editing { get; private set; }
    internal bool Help { get; private set; }
    internal string Query { get; private set; } = string.Empty;
    internal int SelectedHit { get; private set; } = -1;
    internal IReadOnlyList<PagerHit> Hits => _hits;
    internal ReadOnlySpan<char> Input => _input.AsSpan(0, _inputLength);
    internal int InputCursor { get; private set; }

    internal void Resize(int width, int height) {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == Height)
            return;
        if (_source is not null) {
            int anchor = LineCount == 0 ? 0 : ReadLine(Math.Min(Top, LineCount - 1)).Offset + ReadLine(Math.Min(Top, LineCount - 1)).Line.Start;
            bool changedWidth = _width != width;
            _width = width;
            Height = height;
            Document ??= SyntaxHighlighter.PlainText(string.Empty);
            _source.SetWidth(Math.Max(1, Width));
            if (changedWidth) {
                _indexedLines = 0;
                _sourceVersion = -1;
            }
            Top = _source.LineAt(anchor);
            RefreshSource();
            ClampTop();
            Version++;
            return;
        }
        HighlightedDocument? previous = Document;
        int oldStart = previous is null ? 0 : previous.Lines.Span[Top].Start;
        bool widthChanged = _width != width;
        _width = width;
        Height = height;
        if (widthChanged || previous is null || view is TextMateView) {
            Document = view is TextMateView textView
                ? textView.Layout(Math.Max(1, Width), Math.Max(1, Height - FooterRows - (Height >= 7 ? 3 : 0)))
                : view.Layout(Math.Max(1, Width));
            if (previous is not null && !ReferenceEquals(previous, Document)) {
                HighlightedLine oldLine = previous.Lines.Span[Top];
                int length = TerminalCells.Fit(previous.Text.AsSpan(oldLine.Start, oldLine.Length), 12);
                ReadOnlySpan<char> anchor = previous.Text.AsSpan(oldLine.Start, length);
                int target = (int)((long)oldStart * Document.Text.Length / Math.Max(1, previous.Text.Length));
                int closest = -1;
                int cursor = 0;
                while (!anchor.IsEmpty && cursor <= Document.Text.Length - anchor.Length) {
                    int match = Document.Text.AsSpan(cursor).IndexOf(anchor, StringComparison.Ordinal);
                    if (match < 0)
                        break;
                    int offset = cursor + match;
                    if (closest < 0 || Math.Abs(offset - target) < Math.Abs(closest - target))
                        closest = offset;
                    cursor = offset + Math.Max(1, anchor.Length);
                }
                Top = LineAt(closest >= 0 ? closest : target);
                IndexSearch();
            }
        }
        LineNumberDigits = LineNumberGutter.Digits(LineCount);
        ClampTop();
        Version++;
    }

    internal int LineAt(int offset) {
        if (_source is not null)
            return _source.LineAt(offset);
        ReadOnlySpan<HighlightedLine> lines = Document.Lines.Span;
        int low = 0;
        int high = lines.Length;
        while (low + 1 < high) {
            int middle = low + ((high - low) / 2);
            if (lines[middle].Start <= offset)
                low = middle;
            else
                high = middle;
        }
        return low;
    }

    internal void Handle(ConsoleKeyInfo key) {
        bool control = (key.Modifiers & ConsoleModifiers.Control) != 0;
        if (control && key.Key == ConsoleKey.C) {
            ExitRequested = true;
            return;
        }
        if (Editing) {
            Edit(key);
            return;
        }
        if (Help) {
            Help = false;
            Version++;
            if (key.Key is ConsoleKey.Q)
                ExitRequested = true;
            return;
        }
        if (key.KeyChar == '/' || (control && key.Key == ConsoleKey.F)) {
            Follow = false;
            Editing = true;
            _inputLength = Math.Min(Query.Length, _input.Length);
            Query.AsSpan(0, _inputLength).CopyTo(_input);
            InputCursor = _inputLength;
            Version++;
            return;
        }
        if (key.KeyChar == '?') {
            Help = true;
            Version++;
            return;
        }
        int top = Top;
        bool endNavigation = false;
        if (key.KeyChar == 'F' && _source is not null) {
            Follow = true;
            Top = LineCount;
            ClampTop();
            Version++;
            return;
        }
        Follow = false;
        int page = Math.Max(1, ContentRows);
        if (control && key.Key == ConsoleKey.D) {
            Top += Math.Max(1, page / 2);
        }
        else if (control && key.Key == ConsoleKey.U) {
            Top -= Math.Max(1, page / 2);
        }
        else if (key.KeyChar == 'g') {
            Top = 0;
        }
        else if (key.KeyChar == 'G') {
            Top = LineCount;
            endNavigation = true;
        }
        else {
            switch (key.Key) {
                case ConsoleKey.DownArrow or ConsoleKey.J or ConsoleKey.Enter:
                    Top++;
                    break;
                case ConsoleKey.UpArrow or ConsoleKey.K:
                    Top--;
                    break;
                case ConsoleKey.PageDown or ConsoleKey.Spacebar or ConsoleKey.L:
                    Top += page;
                    break;
                case ConsoleKey.PageUp or ConsoleKey.H or ConsoleKey.B:
                    Top -= page;
                    break;
                case ConsoleKey.Home:
                    Top = 0;
                    break;
                case ConsoleKey.End:
                    Top = LineCount;
                    endNavigation = true;
                    break;
                case ConsoleKey.N:
                    MoveMatch((key.Modifiers & ConsoleModifiers.Shift) != 0);
                    return;
                case ConsoleKey.C:
                    SetQuery(string.Empty);
                    return;
                case ConsoleKey.Q or ConsoleKey.Escape:
                    ExitRequested = true;
                    return;
            }
        }
        ClampTop(endNavigation);
        if (Top != top)
            Version++;
    }

    internal void SetQuery(string query) {
        if (query.Length > _input.Length)
            query = query[.._input.Length];
        if (!Query.Equals(query, StringComparison.Ordinal)) {
            Query = query;
            IndexSearch();
        }
        SelectedHit = -1;
        MoveMatch(false);
        Version++;
    }

    private void IndexSearch(int firstLine = 0) {
        if (firstLine == 0) {
            _hits.Clear();
            SelectedHit = -1;
            HitsTruncated = false;
        }
        SearchVersion++;
        int count = LineCount;
        _indexedLines = count;
        if (Query.Length == 0 || HitsTruncated)
            return;
        for (int lineIndex = firstLine; lineIndex < count; lineIndex++) {
            PagerLine row = ReadLine(lineIndex);
            HighlightedLine line = row.Line;
            int cursor = line.Start;
            int end = line.Start + line.Length;
            while (cursor <= end - Query.Length) {
                int index = row.Document.Text.AsSpan(cursor, end - cursor).IndexOf(Query, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;
                cursor += index;
                if (_hits.Count == 100000) {
                    HitsTruncated = true;
                    return;
                }
                _hits.Add(new(lineIndex, checked(cursor + row.Offset), Query.Length));
                cursor += Query.Length;
            }
        }
    }

    internal int FirstHit(int offset) {
        int low = 0;
        int high = _hits.Count;
        while (low < high) {
            int middle = low + ((high - low) / 2);
            if (_hits[middle].Start + _hits[middle].Length <= offset)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private void MoveMatch(bool previous) {
        if (_hits.Count == 0) {
            Version++;
            return;
        }
        if (SelectedHit >= 0) {
            SelectedHit = (SelectedHit + (previous ? _hits.Count - 1 : 1)) % _hits.Count;
        }
        else {
            int first = FirstHit(ReadLine(Top).Line.Start + ReadLine(Top).Offset);
            SelectedHit = previous ? (first + _hits.Count - 1) % _hits.Count : first % _hits.Count;
        }
        Top = _hits[SelectedHit].Line;
        ClampTop();
        SearchVersion++;
        Version++;
    }

    private void ClampTop(bool endNavigation = false) {
        int maximum = Math.Max(0, LineCount - Math.Max(1, ContentRows));
        bool down = endNavigation || Top >= _lastTop;
        bool images = KeepImagesWhole && ImageCount > 0;
        int last = Math.Max(0, LineCount - 1);
        Top = Math.Clamp(Top, 0, !endNavigation && images && _lastTop > maximum && Top > maximum ? last : maximum);
        if (KeepImagesWhole) {
            for (int pass = 0; pass < ImageCount; pass++) {
                int before = Top;
                for (int index = 0; index < ImageCount; index++) {
                    TextBounds bounds = GetImageBounds(index);
                    if (Top > bounds.Row && Top < bounds.Row + bounds.Height) {
                        Top = Math.Clamp(down ? bounds.Row + bounds.Height : bounds.Row, 0, last);
                    }
                }
                if (Top == before)
                    break;
            }
        }
        _lastTop = Top;
    }

    private void Edit(ConsoleKeyInfo key) {
        if (key.Key == ConsoleKey.Enter) {
            Editing = false;
            SetQuery(new string(Input));
            return;
        }
        if (key.Key == ConsoleKey.Escape) {
            Editing = false;
            Version++;
            return;
        }
        switch (key.Key) {
            case ConsoleKey.Home:
                InputCursor = 0;
                break;
            case ConsoleKey.End:
                InputCursor = _inputLength;
                break;
            case ConsoleKey.LeftArrow:
                InputCursor = PreviousCluster();
                break;
            case ConsoleKey.RightArrow:
                if (InputCursor < _inputLength)
                    InputCursor += TerminalCells.NextClusterLength(Input[InputCursor..]);
                break;
            case ConsoleKey.Backspace:
                if (InputCursor > 0) {
                    int start = PreviousCluster();
                    Remove(start, InputCursor - start);
                    InputCursor = start;
                }
                break;
            case ConsoleKey.Delete:
                if (InputCursor < _inputLength)
                    Remove(InputCursor, TerminalCells.NextClusterLength(Input[InputCursor..]));
                break;
            default:
                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.U) {
                    InputCursor = _inputLength = 0;
                }
                else if (!char.IsControl(key.KeyChar) && _inputLength < _input.Length) {
                    _input.AsSpan(InputCursor, _inputLength - InputCursor).CopyTo(_input.AsSpan(InputCursor + 1));
                    _input[InputCursor++] = key.KeyChar;
                    _inputLength++;
                }
                break;
        }
        Version++;
    }

    private int PreviousCluster() {
        int previous = 0;
        int index = 0;
        while (index < InputCursor) {
            previous = index;
            index += TerminalCells.NextClusterLength(Input[index..]);
        }
        return previous;
    }

    private void Remove(int start, int count) {
        _input.AsSpan(start + count, _inputLength - start - count).CopyTo(_input.AsSpan(start));
        _inputLength -= count;
    }
}
