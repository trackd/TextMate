namespace PSTextMate.Terminal;

internal static class PagerHighlighting {
    internal static IRenderable BuildSegmentHighlightRenderable(
        IRenderable renderable,
        string query,
        Style rowStyle,
        Style matchStyle,
        bool highlightLinkedLabelsOnNoDirectMatch = false
    ) => new SegmentHighlightRenderable(renderable, query, rowStyle, matchStyle, highlightLinkedLabelsOnNoDirectMatch);

    internal static string NormalizeText(string? text) {
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .TrimEnd('\n');
    }

    private static List<PagerSearchHit> BuildQueryHits(string plainText, string query) {
        if (string.IsNullOrEmpty(plainText) || string.IsNullOrEmpty(query)) {
            return [];
        }

        var hits = new List<PagerSearchHit>();
        int searchStart = 0;
        while (searchStart <= plainText.Length - query.Length) {
            int hitOffset = plainText.IndexOf(query, searchStart, StringComparison.OrdinalIgnoreCase);
            if (hitOffset < 0) {
                break;
            }

            hits.Add(new PagerSearchHit(0, hitOffset, query.Length, 0, hitOffset));
            searchStart = hitOffset + Math.Max(1, query.Length);
        }

        return hits;
    }

    private sealed class SegmentHighlightRenderable : IRenderable {
        private readonly IRenderable _inner;
        private readonly string _query;
        private readonly Style _rowStyle;
        private readonly Style _matchStyle;
        private readonly bool _highlightLinkedLabelsOnNoDirectMatch;

        public SegmentHighlightRenderable(
            IRenderable inner,
            string query,
            Style rowStyle,
            Style matchStyle,
            bool highlightLinkedLabelsOnNoDirectMatch
        ) {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _query = query ?? string.Empty;
            _rowStyle = rowStyle;
            _matchStyle = matchStyle;
            _highlightLinkedLabelsOnNoDirectMatch = highlightLinkedLabelsOnNoDirectMatch;
        }

        public Measurement Measure(RenderOptions options, int maxWidth)
            => _inner.Measure(options, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
            List<Segment> source = [.. _inner.Render(options, maxWidth)];
            if (source.Count == 0 || string.IsNullOrEmpty(_query)) {
                return source;
            }

            string plainText = BuildPlainText(source);
            if (plainText.Length == 0) {
                return source;
            }

            List<PagerSearchHit> hits = BuildQueryHits(plainText, _query);
            bool hasDirectHits = hits.Count > 0;
            bool highlightLinkedLabels = _highlightLinkedLabelsOnNoDirectMatch
                && !hasDirectHits
                && source.Any(segment => SegmentLinkMatchesQuery(segment, _query));

            if (!hasDirectHits && !highlightLinkedLabels) {
                return source;
            }

            bool[] matchMask = new bool[plainText.Length];
            foreach (PagerSearchHit hit in hits) {
                int start = Math.Clamp(hit.Offset, 0, plainText.Length);
                int length = Math.Clamp(hit.Length, 0, plainText.Length - start);
                for (int i = 0; i < length; i++) {
                    matchMask[start + i] = true;
                }
            }

            bool[] lineHasMatch = BuildLineMatchMask(plainText, hits);
            return RebuildSegmentsWithHighlights(source, matchMask, lineHasMatch, highlightLinkedLabels);
        }

        private static bool SegmentLinkMatchesQuery(Segment segment, string query) {
            if (string.IsNullOrWhiteSpace(query) || segment.IsControlCode || segment.IsLineBreak) {
                return false;
            }

            string? link = segment.Style.Link;
            return !string.IsNullOrWhiteSpace(link)
                && link.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildPlainText(IEnumerable<Segment> segments) {
            StringBuilder builder = StringBuilderPool.Rent();
            try {
                foreach (Segment segment in segments) {
                    if (segment.IsControlCode) {
                        continue;
                    }

                    if (segment.IsLineBreak) {
                        builder.Append('\n');
                        continue;
                    }

                    builder.Append(segment.Text);
                }

                return builder.ToString();
            }
            finally {
                StringBuilderPool.Return(builder);
            }
        }

        private static bool[] BuildLineMatchMask(string plainText, IReadOnlyList<PagerSearchHit> hits) {
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < plainText.Length; i++) {
                if (plainText[i] == '\n' && i + 1 < plainText.Length) {
                    lineStarts.Add(i + 1);
                }
            }

            bool[] lineMatches = new bool[lineStarts.Count == 0 ? 1 : lineStarts.Count];
            foreach (PagerSearchHit hit in hits) {
                int line = ResolveLine(lineStarts, hit.Offset);
                lineMatches[line] = true;
            }

            return lineMatches;
        }

        private static int ResolveLine(List<int> lineStarts, int offset) {
            if (lineStarts.Count == 0) {
                return 0;
            }

            int line = 0;
            for (int i = 1; i < lineStarts.Count; i++) {
                if (lineStarts[i] > offset) {
                    break;
                }

                line = i;
            }

            return line;
        }

        private List<Segment> RebuildSegmentsWithHighlights(
            List<Segment> source,
            bool[] matchMask,
            bool[] lineHasMatch,
            bool highlightLinkedLabels
        ) {
            var output = new List<Segment>(source.Count * 2);
            int absolute = 0;
            int line = 0;
            StringBuilder chunk = StringBuilderPool.Rent();

            try {
                foreach (Segment segment in source) {
                    if (segment.IsControlCode) {
                        output.Add(segment);
                        continue;
                    }

                    if (segment.IsLineBreak) {
                        output.Add(segment);
                        if (absolute < matchMask.Length) {
                            absolute++;
                        }

                        line = Math.Min(line + 1, lineHasMatch.Length - 1);
                        continue;
                    }

                    if (segment.Text.Length == 0) {
                        continue;
                    }

                    bool segmentLinkMatchesQuery = highlightLinkedLabels && SegmentLinkMatchesQuery(segment, _query);

                    chunk.Clear();
                    Style? chunkStyle = null;

                    foreach (char ch in segment.Text) {
                        if (ch == '\n') {
                            FlushChunk(output, chunk, chunkStyle);
                            output.Add(Segment.LineBreak);
                            if (absolute < matchMask.Length) {
                                absolute++;
                            }

                            line = Math.Min(line + 1, lineHasMatch.Length - 1);
                            continue;
                        }

                        bool inMatch = absolute >= 0 && absolute < matchMask.Length && matchMask[absolute];
                        bool inMatchedLine = line >= 0 && line < lineHasMatch.Length && lineHasMatch[line];
                        Style style;
                        if (ch == '│') {
                            // leave borders as is.
                            style = segment.Style;
                        }
                        else if (inMatch || segmentLinkMatchesQuery) {
                            style = _matchStyle;
                        }
                        else if (inMatchedLine) {
                            style = _rowStyle;
                        }
                        else {
                            style = segment.Style;
                        }

                        if (chunkStyle is null || !chunkStyle.Equals(style)) {
                            FlushChunk(output, chunk, chunkStyle);
                            chunkStyle = style;
                        }

                        chunk.Append(ch);
                        absolute++;
                    }

                    FlushChunk(output, chunk, chunkStyle);
                }
            }
            finally {
                StringBuilderPool.Return(chunk);
            }

            return output;
        }

        private static void FlushChunk(List<Segment> output, StringBuilder chunk, Style? style) {
            if (chunk.Length == 0 || style is null) {
                return;
            }

            output.Add(new Segment(chunk.ToString(), style));
            chunk.Clear();
        }
    }

}
