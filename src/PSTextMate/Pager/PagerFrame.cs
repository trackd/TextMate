namespace PSTextMate.Core;

/// <summary>Bounded reusable row buffers and one batched span write for changed terminal rows.</summary>
internal sealed partial class PagerFrame : IDisposable {
    private static readonly TextStyle PositionStyle = TextStyle.Plain.WithDecoration(TextDecoration.Bold);
    private static readonly string[] HelpLines = [
        "TextMate pager", "", "↑ / ↓, j / k       One line", "PgUp / PgDn        One page",
        "Space / l          Next page", "b / h              Previous page", "Ctrl+U / Ctrl+D    Half page",
        "Home / End, g / G  First / last page", "/ or Ctrl+F        Edit search",
        "n / N              Next / previous match", "F                  Follow arriving output",
        "c                  Clear search", "?                  Key bindings",
        "q, Escape, Ctrl+C  Quit", "", "Search: Enter applies, Escape cancels; arrows, Home/End, Delete and Backspace edit.",
        "Press any key to return."
    ];
    private readonly Dictionary<int, int> _rows = [];
    private readonly TextBufferWriter _row = new();
    private readonly TextBufferWriter _keys = new();
    private readonly TextBufferWriter _frame = new(4096);
    private TextBufferWriter[] _previous = [];
    private int[] _previousSlots = [];
    private long[] _previousRevisions = [];
    private int[] _freeSlots = [];
    private int _freeCount;
    private CachedRow[] _cache = [];
    private int _next;
    private bool _invalidate;
    private HighlightedDocument? _document;
    private int _searchVersion = -1;
    private int _width;
    private string _query = string.Empty;
    private int _selectedLine = -1;
    private long _revision;
    private string _panelTop = string.Empty;
    private string _panelBottom = string.Empty;
    private string _searchTop = string.Empty;

    private sealed class CachedRow : IDisposable {
        internal int Line = -1;
        internal long Revision;
        internal readonly TextBufferWriter Text = new();
        public void Dispose() => Text.Dispose();
    }

    internal void Draw(PagerState state, IPagerTerminal terminal) {
        if (_graphicsPainted)
            _invalidate = true;
        if (_previous.Length != state.Height)
            ResizeBuffers(state.Height);
        if (!ReferenceEquals(_document, state.Document) || _query != state.Query || _width != state.Width) {
            if (_width != state.Width) {
                _panelTop = PanelFrame.Border(state.Width);
                _panelBottom = PanelFrame.Border(state.Width, bottom: true);
                _searchTop = PanelFrame.Border(state.Width, "Search");
                _invalidate = true;
            }
            _rows.Clear();
            _next = 0;
            _freeCount = 0;
            foreach (CachedRow slot in _cache)
                slot.Line = -1;
            _document = state.Document;
            _width = state.Width;
        }
        else if (_searchVersion != state.SearchVersion) {
            InvalidateLine(_selectedLine);
            if (state.SelectedHit >= 0)
                InvalidateLine(state.Hits[state.SelectedHit].Line);
        }
        _searchVersion = state.SearchVersion;
        _query = state.Query;
        _selectedLine = state.SelectedHit >= 0 ? state.Hits[state.SelectedHit].Line : -1;
        _frame.Clear();
        for (int row = 0; row < state.ContentRows; row++) {
            if (state.Help) {
                UpdateRow(row + state.SearchRows, row < HelpLines.Length ? Clip(HelpLines[row], state.Width) : []);
            }
            else {
                int line = state.Top + row;
                if (line >= state.LineCount) {
                    UpdateRow(row + state.SearchRows, []);
                }
                else {
                    if (!_rows.TryGetValue(line, out int slotIndex)) {
                        if (_freeCount > 0) {
                            slotIndex = _freeSlots[--_freeCount];
                        }
                        else {
                            slotIndex = _next;
                            _next = (_next + 1) % _cache.Length;
                        }
                        CachedRow slot = _cache[slotIndex];
                        if (slot.Line >= 0)
                            _rows.Remove(slot.Line);
                        slot.Text.Clear();
                        RenderLine(state, line, slot.Text);
                        slot.Line = line;
                        slot.Revision = ++_revision;
                        _rows.Add(line, slotIndex);
                    }
                    UpdateCachedRow(row + state.SearchRows, slotIndex);
                }
            }
        }
        int footerStart = state.Height - state.FooterRows;
        if (state.SearchRows == 3) {
            UpdateRow(0, _searchTop);
            _row.Clear();
            PanelFrame.WriteRowStart(_row, state.Width, 1);
            int visible = WriteSearchInput(state, _row);
            _row.Write(VT.Reset);
            PanelFrame.WriteRowEnd(_row, state.Width, visible, 1);
            UpdateRow(1, _row.WrittenSpan);
            UpdateRow(2, _panelBottom);
        }
        if (state.FooterRows == 3) {
            UpdateRow(footerStart, _panelTop);
            WriteRichFooter(state);
            UpdateRow(footerStart + 1, _row.WrittenSpan);
            UpdateRow(footerStart + 2, _panelBottom);
        }
        else {
            _row.Clear();
            if (state.Editing) {
                _row.Write(VT.Invert);
                WriteSearchInput(state, _row);
                _row.Write(VT.Reset);
            }
            else {
                WriteStatus(state);
                WriteHelp(_row, Clip(_keys.WrittenSpan, state.Width));
            }
            UpdateRow(state.Height - 1, _row.WrittenSpan);
        }
        AppendGraphics(state, PagerTerminal.CanDraw(terminal));
        _invalidate = false;
        if (_frame.Length == 0)
            return;
        try {
            terminal.Write(VT.BeginSynchronizedOutput);
            terminal.Write(_frame.WrittenSpan);
        }
        finally {
            terminal.Write(VT.EndSynchronizedOutput);
            terminal.Flush();
        }
    }

    private void ResizeBuffers(int height) {
        foreach (TextBufferWriter previous in _previous)
            previous.Dispose();
        foreach (CachedRow slot in _cache)
            slot.Dispose();
        _previous = new TextBufferWriter[height];
        _previousSlots = new int[height];
        Array.Fill(_previousSlots, -1);
        _previousRevisions = new long[height];
        for (int index = 0; index < height; index++)
            _previous[index] = new TextBufferWriter();
        _cache = new CachedRow[Math.Max(16, height * 4)];
        _freeSlots = new int[_cache.Length];
        _freeCount = 0;
        for (int index = 0; index < _cache.Length; index++)
            _cache[index] = new CachedRow();
        _rows.Clear();
        _rows.EnsureCapacity(_cache.Length);
        _next = 0;
        _invalidate = true;
    }

    private void UpdateRow(int row, ReadOnlySpan<char> value) {
        TextBufferWriter previous = _previous[row];
        if (!_invalidate && _previousSlots[row] < 0 && previous.WrittenSpan.SequenceEqual(value))
            return;
        _previousSlots[row] = -1;
        previous.Clear();
        previous.Write(value);
        EmitRow(row, value);
    }

    private void UpdateCachedRow(int row, int slotIndex) {
        CachedRow slot = _cache[slotIndex];
        if (!_invalidate && _previousSlots[row] == slotIndex && _previousRevisions[row] == slot.Revision)
            return;
        _previousSlots[row] = slotIndex;
        _previousRevisions[row] = slot.Revision;
        EmitRow(row, slot.Text.WrittenSpan);
    }

    private void InvalidateLine(int line) {
        if (_rows.Remove(line, out int slotIndex)) {
            _cache[slotIndex].Line = -1;
            _freeSlots[_freeCount++] = slotIndex;
        }
    }

    private void EmitRow(int row, ReadOnlySpan<char> value) => VT.WriteRow(_frame, row + 1, value);

    private static void RenderLine(PagerState state, int index, TextBufferWriter output) {
        PagerLine row = state.ReadLine(index);
        HighlightedDocument document = row.Document;
        HighlightedLine line = row.Line;
        int available = state.Width;
        if (document.ShowLineNumbers) {
            Span<char> gutter = stackalloc char[13];
            int length = LineNumberGutter.Format(gutter, index + 1, document.LineCount);
            int fit = TerminalCells.Fit(gutter[..length], available);
            output.Write(gutter[..fit]);
            available -= TerminalCells.Measure(gutter[..fit]);
        }
        int end = line.Start + TerminalCells.Fit(document.Text.AsSpan(line.Start, line.Length), available);
        int imageStart = -1;
        int imageEnd = -1;
        FindImage(line.Start);
        int cursor = line.Start;
        int hitIndex = state.FirstHit(cursor + row.Offset);
        foreach (StyledRange range in document.Ranges.Span.Slice(line.FirstRange, line.RangeCount)) {
            if (cursor >= end)
                break;
            if (range.Start > cursor)
                Piece(cursor, Math.Min(range.Start, end), PresentedTextBuilder.Plain);
            if (range.Start >= end)
                break;
            if (range.Start + range.Length > cursor)
                Piece(Math.Max(cursor, range.Start), Math.Min(end, range.Start + range.Length), range.Style);
        }
        if (cursor < end)
            Piece(cursor, end, PresentedTextBuilder.Plain);

        void FindImage(int position) {
            imageStart = -1;
            imageEnd = -1;
            if (!state.KeepImagesWhole)
                return;
            for (int image = 0; image < state.ImageCount; image++) {
                TextBounds bounds = state.GetImageBounds(image);
                if (index < bounds.Row || index >= bounds.Row + bounds.Height)
                    continue;
                int prefix = TerminalCells.Fit(document.Text.AsSpan(line.Start, line.Length),
                    Math.Max(0, bounds.Column - (state.Width - available)));
                int from = line.Start + prefix;
                int to = from + TerminalCells.Fit(document.Text.AsSpan(from, line.Length - prefix), bounds.Width);
                if (to <= position || (imageStart >= 0 && from >= imageStart))
                    continue;
                imageStart = from;
                imageEnd = to;
            }
        }
        void Piece(int start, int finish, TextStyle style) {
            cursor = start;
            while (cursor < finish) {
                if (imageStart >= 0 && cursor >= imageEnd)
                    FindImage(cursor);
                while (hitIndex < state.Hits.Count && state.Hits[hitIndex].Start + state.Hits[hitIndex].Length <= cursor + row.Offset)
                    hitIndex++;
                PagerHit? hit = hitIndex < state.Hits.Count ? state.Hits[hitIndex] : null;
                bool matches = hit is { } current && current.Start <= cursor + row.Offset;
                int next = hit is { } match ? Math.Min(finish, matches ? match.Start + match.Length - row.Offset : match.Start - row.Offset) : finish;
                if (imageStart >= 0 && cursor < imageEnd) {
                    if (cursor >= imageStart) {
                        next = Math.Min(next, imageEnd);
                        output.Write(VT.Reset);
                        TextGeometry.WritePadding(output, TerminalCells.Measure(document.Text.AsSpan(cursor, next - cursor)));
                        cursor = next;
                        continue;
                    }
                    next = Math.Min(next, imageStart);
                }
                TextStyle effective = matches ? style with { Foreground = 0, Background = hitIndex == state.SelectedHit ? 0xffe08a : 0xffa500, Hyperlink = null } : style;
                TextOutput.Write(output, document.Text.AsSpan(cursor, next - cursor), effective);
                cursor = next;
            }
        }
    }

    private void WriteStatus(PagerState state) {
        int end = Math.Min(state.LineCount, state.Top + Math.Max(1, state.ContentRows));
        _keys.Clear();
        _keys.Write(' ');
        _keys.Write(state.Top + 1);
        _keys.Write('-');
        _keys.Write(end);
        _keys.Write('/');
        _keys.Write(state.LineCount);
        _keys.Write("  ");
        _keys.Write((int)((long)end * 100 / Math.Max(1, state.LineCount)));
        _keys.Write('%');
        if (state.IsStreaming)
            _keys.Write(state.Follow ? " following" : " streaming");
        if (state.Query.Length > 0) {
            _keys.Write("  [");
            _keys.Write(state.SelectedHit + 1);
            _keys.Write('/');
            _keys.Write(state.Hits.Count);
            if (state.HitsTruncated)
                _keys.Write('+');
            _keys.Write(']');
            if (state.SelectedHit >= 0) {
                PagerHit hit = state.Hits[state.SelectedHit];
                PagerLine row = state.ReadLine(hit.Line);
                HighlightedLine line = row.Line;
                _keys.Write(" L");
                _keys.Write(hit.Line + 1);
                _keys.Write(":C");
                _keys.Write(TerminalCells.Measure(row.Document.Text.AsSpan(line.Start, hit.Start - row.Offset - line.Start)) + 1);
            }
            _keys.Write("  /");
            _keys.Write(state.Query);
        }
        else {
            _keys.Write("  ? help  q quit");
        }
    }

    private void WriteRichFooter(PagerState state) {
        int end = Math.Min(state.LineCount, state.Top + Math.Max(1, state.ContentRows));
        int digits = state.LineNumberDigits;
        int statusWidth = (digits * 3) + 4;
        int chartWidth = Math.Clamp(state.Width / 4, 14, 40);
        Span<int> columns = stackalloc int[3];
        TextGeometry.DistributeColumns(state.Width - 2, 0, [0, statusWidth, chartWidth], columns, out _);
        int keysWidth = columns[0];
        _keys.Clear();
        _keys.Write(state.Editing ? "Search: Enter Apply  Esc Cancel"
            : state.IsStreaming ? state.Follow ? "Following  ? help" : "Streaming  F follow  ? help" : "Press ? for help");
        if (state.Query.Length > 0) {
            _keys.Write("  /");
            _keys.Write(state.Query);
            _keys.Write(" [");
            _keys.Write(state.SelectedHit + 1);
            _keys.Write('/');
            _keys.Write(state.Hits.Count);
            if (state.HitsTruncated)
                _keys.Write('+');
            _keys.Write(']');
        }
        int barColumns = Math.Max(1, chartWidth - 2);
        int filled = TextProgress.Filled(barColumns, end, state.LineCount, 4);
        _row.Clear();
        PanelFrame.WriteRowStart(_row, state.Width);
        ReadOnlySpan<char> clipped = Clip(_keys.WrittenSpan, keysWidth);
        WriteHelp(_row, clipped);
        TextGeometry.WritePadding(_row, Math.Max(0, keysWidth - TerminalCells.Measure(clipped)));
        VT.WriteStyle(_row, PositionStyle);
        TextGeometry.WritePadding(_row, 2);
        WriteNumber(state.Top + 1, digits);
        _row.Write('-');
        WriteNumber(end, digits);
        _row.Write('/');
        WriteNumber(state.LineCount, digits);
        _row.Write(VT.Reset + "  ");
        VT.WriteIndexedColor(_row, 10);
        TextGeometry.WritePadding(_row, filled, '█');
        TextGeometry.WritePadding(_row, barColumns - filled);
        _row.Write(VT.Reset);
        PanelFrame.WriteRowEnd(_row, state.Width, state.Width - 2);
    }

    private void WriteNumber(int value, int digits) {
        TextGeometry.WritePadding(_row, digits - LineNumberGutter.Digits(Math.Max(1, value)));
        _row.Write(value);
    }

    private static int WriteSearchInput(PagerState state, TextBufferWriter output) {
        if (state.Width < 3) {
            if (state.Width > 0)
                output.Write('/');
            return state.Width > 0 ? 1 : 0;
        }
        int available = Math.Max(0, state.Width - (state.SearchRows == 3 ? 5 : 2));
        int first = 0;
        while (first < state.InputCursor && TerminalCells.Measure(state.Input[first..state.InputCursor]) >= available)
            first += TerminalCells.NextClusterLength(state.Input[first..]);
        int before = TerminalCells.Fit(state.Input[first..state.InputCursor], available);
        int after = state.InputCursor < state.Input.Length ? TerminalCells.NextClusterLength(state.Input[state.InputCursor..]) : 0;
        ReadOnlySpan<char> prefix = state.Input.Slice(first, before);
        ReadOnlySpan<char> cursor = after == 0 ? " " : state.Input.Slice(state.InputCursor, after);
        int prefixCells = TerminalCells.Measure(prefix);
        if (TerminalCells.Measure(cursor) > available - prefixCells)
            cursor = " ";
        int cursorCells = TerminalCells.Measure(cursor);
        int remaining = Math.Max(0, available - prefixCells - cursorCells);
        ReadOnlySpan<char> tail = state.Input[(state.InputCursor + after)..];
        ReadOnlySpan<char> suffix = tail[..TerminalCells.Fit(tail, remaining)];
        output.Write('/');
        output.Write(prefix);
        VT.WriteStyle(output, TextStyle.Plain.WithDecoration(TextDecoration.Underline));
        output.Write(after == 0 ? "_" : cursor);
        output.Write(VT.Reset);
        output.Write(suffix);
        return 1 + prefixCells + cursorCells + TerminalCells.Measure(suffix);
    }

    private static void WriteHelp(TextBufferWriter output, ReadOnlySpan<char> text) {
        VT.WriteIndexedColor(output, 8);
        TerminalText.WriteSafe(output, text);
        output.Write(VT.Reset);
    }

    private static ReadOnlySpan<char> Clip(ReadOnlySpan<char> text, int width) => text[..TerminalCells.Fit(text, Math.Max(0, width))];

    public void Dispose() {
        foreach (TextBufferWriter previous in _previous)
            previous.Dispose();
        foreach (CachedRow slot in _cache)
            slot.Dispose();
        _row.Dispose();
        _keys.Dispose();
        _frame.Dispose();
    }
}
