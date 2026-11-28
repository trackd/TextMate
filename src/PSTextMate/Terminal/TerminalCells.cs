namespace PSTextMate.Core;

/// <summary>Cell measurement shared by presentation backends; clusters are never split.</summary>
internal static class TerminalCells {
    private static readonly SearchValues<char> PrintableAscii = CreatePrintableAscii();

    private static SearchValues<char> CreatePrintableAscii() {
        Span<char> values = stackalloc char[95];
        for (int index = 0; index < values.Length; index++)
            values[index] = (char)(' ' + index);
        return SearchValues.Create(values);
    }

    internal static int Measure(ReadOnlySpan<char> text) {
        if (Ascii.IsValid(text))
            return text.Length;
        int width = 0;
        while (!text.IsEmpty) {
            if (text.Length > 1 && text[0] is >= ' ' and < '\u007f' && text[1] is >= ' ' and < '\u007f') {
                int ascii = text.IndexOfAnyExcept(PrintableAscii);
                if (ascii < 0)
                    return width + text.Length;
                width += ascii - 1;
                text = text[(ascii - 1)..];
            }
            var cluster = new UnicodeGraphemes.GraphemeState(0);
            ReadCluster(ref cluster, text);
            width += cluster.Width;
            text = text[cluster.Len..];
        }
        return width;
    }

    internal static int Fit(ReadOnlySpan<char> text, int columns) {
        if (Ascii.IsValid(text))
            return Math.Min(text.Length, Math.Max(0, columns));
        int offset = 0;
        while (offset < text.Length) {
            var cluster = new UnicodeGraphemes.GraphemeState(offset);
            ReadCluster(ref cluster, text);
            int width = cluster.Width;
            if (width > columns)
                break;
            columns -= width;
            offset += cluster.Len;
        }
        return offset;
    }

    internal static int NextClusterLength(ReadOnlySpan<char> text) {
        if (text.IsEmpty)
            return 0;
        if (text[0] is >= ' ' and < '\u007f' && (text.Length == 1 || text[1] is >= ' ' and < '\u007f'))
            return 1;
        var cluster = new UnicodeGraphemes.GraphemeState(0);
        ReadCluster(ref cluster, text);
        return cluster.Len;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadCluster(ref UnicodeGraphemes.GraphemeState cluster, ReadOnlySpan<char> text) {
        int offset = cluster.Beg;
        UnicodeGraphemes.Next(ref cluster, text);
        // The compact terminal tables omit Indic conjunct rule GB9c.
        // Preserve the runtime's segmentation policy for these script blocks.
        char first = text[offset];
        if (first is (>= '\u0900' and <= '\u1cff') or (>= '\ua800' and <= '\uabff')
            or (>= '\ud804' and <= '\ud807')) {
            ReadRuntimeCluster(ref cluster, text, offset);
        }
    }

    private static void ReadRuntimeCluster(ref UnicodeGraphemes.GraphemeState cluster, ReadOnlySpan<char> text, int offset) {
        int length = StringInfo.GetNextTextElementLength(text[offset..]);
        int end = offset + length;
        while (cluster.Beg + cluster.Len < end) {
            int width = cluster.Width;
            UnicodeGraphemes.Next(ref cluster, text[..end]);
            cluster.Width = Math.Min(2, width + cluster.Width);
        }
        cluster.Beg = offset;
        cluster.Len = length;
    }
}
