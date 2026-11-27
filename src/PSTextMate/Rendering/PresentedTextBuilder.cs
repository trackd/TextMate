namespace PSTextMate.Core;

internal sealed class PresentedTextBuilder {
    private List<PlacedImage>? _images;
    internal void AddImage(int offset, TerminalImage image) => (_images ??= []).Add(new(offset, image));
    internal static readonly TextStyle Plain = new(-1, -1, TextDecoration.None);
    private readonly StringBuilder _text = new();
    private readonly List<StyledRange> _ranges = [];
    private bool _skipLineFeed;
    internal int SourcePosition { get; set; }
    internal int Length => _text.Length;
    internal int LineCount { get; private set; } = 1;

    internal void Append(ReadOnlySpan<char> text, TextStyle style) {
        int start = _text.Length;
        if (!_skipLineFeed && text.IndexOfAny('\r', '\t') < 0 && !TerminalText.HasControls(text)) {
            _text.Append(text);
            LineCount += text.Count('\n');
            AddRange(start, text.Length, style);
            return;
        }
        foreach (char character in text) {
            if (_skipLineFeed && character == '\n') {
                _skipLineFeed = false;
                continue;
            }
            _skipLineFeed = false;
            if (character == '\r') {
                _text.Append('\n');
                LineCount++;
                _skipLineFeed = true;
            }
            else if (character == '\t') {
                _text.Append("    ");
            }
            else if (character != '\n' && char.IsControl(character)) {
                _text.Append(TerminalText.Visible(character));
            }
            else {
                _text.Append(character);
                if (character == '\n')
                    LineCount++;
            }
        }
        AddRange(start, _text.Length - start, style);
    }

    // Layout padding and borders are printable characters and need no control normalization.
    internal void AppendRepeated(char character, int count, TextStyle style) {
        if (count <= 0)
            return;
        int start = _text.Length;
        _skipLineFeed = false;
        _text.Append(character, count);
        AddRange(start, count, style);
    }

    private void AddRange(int start, int length, TextStyle style) {
        if (length == 0)
            return;
        if (_ranges.Count > 0 && _ranges[^1].Style == style && _ranges[^1].Start + _ranges[^1].Length == start) {
            StyledRange previous = _ranges[^1];
            _ranges[^1] = previous with { Length = previous.Length + length };
        }
        else {
            _ranges.Add(new(start, length, style));
        }
    }

    internal void Append(HighlightedDocument document) {
        foreach (PlacedImage image in document.Images.Span)
            AddImage(_text.Length + image.Start, image.Image);
        int cursor = 0;
        foreach (StyledRange range in document.Ranges.Span) {
            Append(document.Text.AsSpan(cursor, range.Start - cursor), Plain);
            Append(document.Text.AsSpan(range.Start, range.Length), range.Style);
            cursor = range.Start + range.Length;
        }
        Append(document.Text.AsSpan(cursor), Plain);
    }

    internal void AppendSlice(HighlightedDocument document, int start, int length) {
        foreach (PlacedImage image in document.Images.Span) {
            if (image.Start >= start && (image.Start < start + length || (length == 0 && image.Start == start)))
                AddImage(_text.Length + image.Start - start, image.Image);
        }
        int cursor = start;
        int end = start + length;
        ReadOnlySpan<StyledRange> ranges = document.Ranges.Span;
        int low = 0, high = ranges.Length;
        while (low < high) {
            int middle = low + ((high - low) / 2);
            if (ranges[middle].Start + ranges[middle].Length <= start)
                low = middle + 1;
            else
                high = middle;
        }
        for (int index = low; index < ranges.Length; index++) {
            StyledRange range = ranges[index];
            if (range.Start >= end)
                break;
            int from = Math.Max(cursor, range.Start);
            int to = Math.Min(end, range.Start + range.Length);
            if (from > cursor)
                Append(document.Text.AsSpan(cursor, from - cursor), Plain);
            if (to > from)
                Append(document.Text.AsSpan(from, to - from), range.Style);
            cursor = to;
        }
        if (cursor < end)
            Append(document.Text.AsSpan(cursor, end - cursor), Plain);
    }

    internal void NewLine() => Append("\n", Plain);

    internal HighlightedDocument Build(HighlightedDocument original) {
        string text = _text.ToString();
        var lines = new List<HighlightedLine>();
        var ranges = new List<StyledRange>(_ranges.Count);
        int start = 0;
        int rangeIndex = 0;
        while (start <= text.Length) {
            int length = text.AsSpan(start).IndexOfAny('\r', '\n');
            if (length < 0)
                length = text.Length - start;
            int end = start + length;
            int first = ranges.Count;
            while (rangeIndex < _ranges.Count && _ranges[rangeIndex].Start < end) {
                StyledRange range = _ranges[rangeIndex];
                int from = Math.Max(start, range.Start);
                int to = Math.Min(end, range.Start + range.Length);
                if (to > from)
                    ranges.Add(new(from, to - from, range.Style));
                if (range.Start + range.Length > end)
                    break;
                rangeIndex++;
            }
            lines.Add(new(start, length, first, ranges.Count - first));
            if (end == text.Length)
                break;
            start = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
            while (rangeIndex < _ranges.Count && _ranges[rangeIndex].Start + _ranges[rangeIndex].Length <= start)
                rangeIndex++;
        }
        return original.WithPresentation(text, [.. ranges], [.. lines], _images?.ToArray());
    }
}
