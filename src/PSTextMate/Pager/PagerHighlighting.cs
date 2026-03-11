namespace PSTextMate.Terminal;

internal static class PagerHighlighting {
    private static readonly FieldInfo? s_panelChildField = typeof(Panel).GetField("_child", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static Paragraph BuildHighlightedTextRenderable(
        string plainText,
        IReadOnlyList<PagerSearchHit> hits,
        Style rowStyle,
        Style matchStyle
    ) {
        var result = new Paragraph();
        if (plainText.Length == 0) {
            return result;
        }

        if (hits.Count == 0) {
            result.Append(plainText, rowStyle);
            return result;
        }

        int position = 0;
        foreach (PagerSearchHit hit in hits.OrderBy(static h => h.Offset)) {
            int start = Math.Clamp(hit.Offset, 0, plainText.Length);
            int length = Math.Clamp(hit.Length, 0, plainText.Length - start);
            if (length <= 0 || start < position) {
                continue;
            }

            if (start > position) {
                result.Append(plainText[position..start], rowStyle);
            }

            result.Append(plainText.Substring(start, length), matchStyle);
            position = start + length;
        }

        if (position < plainText.Length) {
            result.Append(plainText[position..], rowStyle);
        }

        return result;
    }

    internal static bool TryBuildStructuredHighlightRenderable(
        IRenderable renderable,
        string query,
        string rowStyle,
        string matchStyle,
        IReadOnlyList<PagerSearchHit> indexedHits,
        out IRenderable highlighted
    ) {
        highlighted = renderable;
        if (string.IsNullOrEmpty(query)) {
            return false;
        }

        if (renderable is Table table) {
            highlighted = CloneTableWithHighlight(table, query, rowStyle, matchStyle, indexedHits);
            return true;
        }

        if (renderable is Grid grid) {
            highlighted = CloneGridWithHighlight(grid, query, rowStyle, matchStyle, indexedHits);
            return true;
        }

        if (renderable is Panel panel) {
            highlighted = ClonePanelWithHighlight(panel, query, rowStyle, matchStyle);
            return true;
        }

        return false;
    }

    internal static bool IsStructuredRowHighlightCandidate(IRenderable renderable)
        => renderable is Table or Grid or Panel;

    internal static IRenderable BuildSegmentHighlightRenderable(
        IRenderable renderable,
        string query,
        Style rowStyle,
        Style matchStyle
    ) => new SegmentHighlightRenderable(renderable, query, rowStyle, matchStyle);


    internal static string NormalizeText(string? text) {
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .TrimEnd('\n');
    }

    private static Table CloneTableWithHighlight(
        Table source,
        string query,
        string rowStyle,
        string matchStyle,
        IReadOnlyList<PagerSearchHit> indexedHits
    ) {
        HashSet<int> matchedRowsFromHits = ResolveTableRowsFromHitLines(source, indexedHits);
        bool[] rowCellMatches = [.. source.Rows.Select(row => row.Any(cell => RenderableContainsQuery(cell, query)))];
        bool hasAnyRowCellMatch = rowCellMatches.Any(static match => match);

        var clone = new Table {
            Border = source.Border,
            BorderStyle = source.BorderStyle,
            UseSafeBorder = source.UseSafeBorder,
            ShowHeaders = source.ShowHeaders,
            ShowRowSeparators = source.ShowRowSeparators,
            ShowFooters = source.ShowFooters,
            Expand = source.Expand,
            Width = source.Width,
            Title = source.Title is null ? null : new TableTitle(source.Title.Text, source.Title.Style),
            Caption = source.Caption is null ? null : new TableTitle(source.Caption.Text, source.Caption.Style)
        };

        foreach (TableColumn sourceColumn in source.Columns) {
            IRenderable header = HighlightRenderableNode(sourceColumn.Header, query, applyRowStyle: false, rowStyle, matchStyle);
            IRenderable? footer = sourceColumn.Footer is null
                ? null
                : HighlightRenderableNode(sourceColumn.Footer, query, applyRowStyle: false, rowStyle, matchStyle);
            var column = new TableColumn(header) {
                Width = sourceColumn.Width,
                Padding = sourceColumn.Padding,
                NoWrap = sourceColumn.NoWrap,
                Alignment = sourceColumn.Alignment,
                Footer = footer
            };

            clone.AddColumn(column);
        }

        int rowIndex = 0;
        foreach (TableRow sourceRow in source.Rows) {
            string[] cellTexts = [.. sourceRow.Select(ExtractRenderableText).Select(NormalizeText)];
            bool rowHasCellMatch = rowCellMatches[rowIndex];
            bool rowHasIndexedContextMatch = !hasAnyRowCellMatch && matchedRowsFromHits.Contains(rowIndex);
            bool rowHasMatch = rowHasCellMatch || rowHasIndexedContextMatch;

            var rowItems = new List<IRenderable>();
            foreach (IRenderable sourceCell in sourceRow) {
                rowItems.Add(HighlightRenderableNode(sourceCell, query, rowHasMatch, rowStyle, matchStyle));
            }

            clone.AddRow(rowItems);
            rowIndex++;
        }

        return clone;
    }

    private static bool RenderableContainsQuery(IRenderable renderable, string query) {
        if (string.IsNullOrEmpty(query)) {
            return false;
        }

        string plainText = NormalizeText(ExtractRenderableText(renderable));
        return plainText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static Grid CloneGridWithHighlight(
        Grid source,
        string query,
        string rowStyle,
        string matchStyle,
        IReadOnlyList<PagerSearchHit> indexedHits
    ) {
        HashSet<int> matchedRowsFromHits = ResolveGridRowsFromHitLines(source, indexedHits);
        bool[] rowCellMatches = [.. source.Rows.Select(row => row.Any(cell => RenderableContainsQuery(cell, query)))];
        bool hasAnyRowCellMatch = rowCellMatches.Any(static match => match);

        var clone = new Grid {
            Expand = source.Expand,
            Width = source.Width
        };

        foreach (GridColumn sourceColumn in source.Columns) {
            clone.AddColumn(new GridColumn {
                Width = sourceColumn.Width,
                NoWrap = sourceColumn.NoWrap,
                Padding = sourceColumn.Padding,
                Alignment = sourceColumn.Alignment
            });
        }

        int rowIndex = 0;
        foreach (GridRow sourceRow in source.Rows) {
            string[] cellTexts = [.. sourceRow.Select(ExtractRenderableText).Select(NormalizeText)];
            bool rowHasCellMatch = rowCellMatches[rowIndex];
            bool rowHasIndexedContextMatch = !hasAnyRowCellMatch && matchedRowsFromHits.Contains(rowIndex);
            bool rowHasMatch = rowHasCellMatch || rowHasIndexedContextMatch;

            IRenderable[] rowItems = [.. sourceRow.Select(cell => HighlightRenderableNode(cell, query, rowHasMatch, rowStyle, matchStyle))];
            clone.AddRow(rowItems);
            rowIndex++;
        }

        return clone;
    }

    private static HashSet<int> ResolveTableRowsFromHitLines(Table source, IReadOnlyList<PagerSearchHit> indexedHits) {
        var matchedRows = new HashSet<int>();
        if (indexedHits.Count == 0 || source.Rows.Count == 0) {
            return matchedRows;
        }

        Table probe = BuildTableSkeleton(source);
        int previousLineCount = CountRenderableLines(probe);

        int rowIndex = 0;
        foreach (TableRow sourceRow in source.Rows) {
            probe.AddRow([.. sourceRow]);
            int currentLineCount = CountRenderableLines(probe);
            int rowStartLine = previousLineCount;
            int rowEndLine = Math.Max(previousLineCount, currentLineCount - 1);

            bool hitInRow = indexedHits.Any(hit => hit.Line >= rowStartLine && hit.Line <= rowEndLine);
            if (hitInRow) {
                matchedRows.Add(rowIndex);
            }

            previousLineCount = currentLineCount;
            rowIndex++;
        }

        return matchedRows;
    }

    private static HashSet<int> ResolveGridRowsFromHitLines(Grid source, IReadOnlyList<PagerSearchHit> indexedHits) {
        var matchedRows = new HashSet<int>();
        if (indexedHits.Count == 0 || source.Rows.Count == 0) {
            return matchedRows;
        }

        var probe = new Grid {
            Expand = source.Expand,
            Width = source.Width
        };

        foreach (GridColumn sourceColumn in source.Columns) {
            probe.AddColumn(new GridColumn {
                Width = sourceColumn.Width,
                NoWrap = sourceColumn.NoWrap,
                Padding = sourceColumn.Padding,
                Alignment = sourceColumn.Alignment
            });
        }

        int previousLineCount = CountRenderableLines(probe);
        int rowIndex = 0;
        foreach (GridRow sourceRow in source.Rows) {
            probe.AddRow([.. sourceRow]);
            int currentLineCount = CountRenderableLines(probe);
            int rowStartLine = previousLineCount;
            int rowEndLine = Math.Max(previousLineCount, currentLineCount - 1);

            bool hitInRow = indexedHits.Any(hit => hit.Line >= rowStartLine && hit.Line <= rowEndLine);
            if (hitInRow) {
                matchedRows.Add(rowIndex);
            }

            previousLineCount = currentLineCount;
            rowIndex++;
        }

        return matchedRows;
    }

    private static Table BuildTableSkeleton(Table source) {
        var probe = new Table {
            Border = source.Border,
            BorderStyle = source.BorderStyle,
            UseSafeBorder = source.UseSafeBorder,
            ShowHeaders = source.ShowHeaders,
            ShowRowSeparators = source.ShowRowSeparators,
            ShowFooters = source.ShowFooters,
            Expand = source.Expand,
            Width = source.Width,
            Title = source.Title is null ? null : new TableTitle(source.Title.Text, source.Title.Style),
            Caption = source.Caption is null ? null : new TableTitle(source.Caption.Text, source.Caption.Style)
        };

        foreach (TableColumn sourceColumn in source.Columns) {
            probe.AddColumn(new TableColumn(sourceColumn.Header) {
                Width = sourceColumn.Width,
                Padding = sourceColumn.Padding,
                NoWrap = sourceColumn.NoWrap,
                Alignment = sourceColumn.Alignment,
                Footer = sourceColumn.Footer
            });
        }

        return probe;
    }

    private static Panel ClonePanelWithHighlight(
        Panel source,
        string query,
        string rowStyle,
        string matchStyle
    ) {
        if (!TryGetPanelChild(source, out IRenderable child)) {
            return source;
        }

        var parsedRowStyle = Style.Parse(rowStyle);
        var parsedMatchStyle = Style.Parse(matchStyle);
        IRenderable highlightedChild = IsStructuredRowHighlightCandidate(child)
            && TryBuildStructuredHighlightRenderable(child, query, rowStyle, matchStyle, [], out IRenderable structuredChild)
            ? structuredChild
            : new SegmentHighlightRenderable(child, query, parsedRowStyle, parsedMatchStyle);

        // Preserve structured semantics for nested tables/grids inside panels.

        return new Panel(highlightedChild) {
            Border = source.Border,
            UseSafeBorder = source.UseSafeBorder,
            BorderStyle = source.BorderStyle,
            Expand = source.Expand,
            Padding = source.Padding,
            Header = source.Header,
            Width = source.Width,
            Height = source.Height
        };
    }

    private static bool TryGetPanelChild(Panel panel, out IRenderable child) {
        if (s_panelChildField?.GetValue(panel) is IRenderable renderable) {
            child = renderable;
            return true;
        }

        child = Text.Empty;
        return false;
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

    private sealed class SegmentHighlightRenderable : Renderable {
        private readonly IRenderable _inner;
        private readonly string _query;
        private readonly Style _rowStyle;
        private readonly Style _matchStyle;

        public SegmentHighlightRenderable(IRenderable inner, string query, Style rowStyle, Style matchStyle) {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _query = query ?? string.Empty;
            _rowStyle = rowStyle;
            _matchStyle = matchStyle;
        }

        protected override Measurement Measure(RenderOptions options, int maxWidth)
            => _inner.Measure(options, maxWidth);

        protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
            List<Segment> source = [.. _inner.Render(options, maxWidth)];
            if (source.Count == 0 || string.IsNullOrEmpty(_query)) {
                return source;
            }

            string plainText = BuildPlainText(source);
            if (plainText.Length == 0) {
                return source;
            }

            List<PagerSearchHit> hits = BuildQueryHits(plainText, _query);
            if (hits.Count == 0) {
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
            return RebuildSegmentsWithHighlights(source, matchMask, lineHasMatch);
        }

        private static string BuildPlainText(IEnumerable<Segment> segments) {
            var builder = new StringBuilder();
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

        private List<Segment> RebuildSegmentsWithHighlights(List<Segment> source, bool[] matchMask, bool[] lineHasMatch) {
            var output = new List<Segment>(source.Count * 2);
            int absolute = 0;
            int line = 0;

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

                var chunk = new StringBuilder();
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
                    Style style = inMatch ? _matchStyle : inMatchedLine ? _rowStyle : segment.Style;

                    if (chunkStyle is null || !chunkStyle.Equals(style)) {
                        FlushChunk(output, chunk, chunkStyle);
                        chunkStyle = style;
                    }

                    chunk.Append(ch);
                    absolute++;
                }

                FlushChunk(output, chunk, chunkStyle);
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

    private static int CountRenderableLines(IRenderable renderable) {
        try {
            string rendered = Writer.WriteToString(renderable, width: 200);
            if (string.IsNullOrEmpty(rendered)) {
                return 0;
            }

            int lines = 1;
            foreach (char ch in rendered) {
                if (ch == '\n') {
                    lines++;
                }
            }

            return lines;
        }
        catch {
            return 0;
        }
    }

    private static IRenderable HighlightRenderableNode(
        IRenderable renderable,
        string query,
        bool applyRowStyle,
        string rowStyle,
        string matchStyle
    ) => HighlightLeafRenderable(renderable, query, applyRowStyle, rowStyle, matchStyle);

    private static IRenderable HighlightLeafRenderable(
        IRenderable renderable,
        string query,
        bool applyRowStyle,
        string rowStyle,
        string matchStyle
    ) {
        string plainText = NormalizeText(ExtractRenderableText(renderable));
        if (plainText.Length == 0) {
            return renderable;
        }

        if (string.IsNullOrEmpty(query)) {
            return applyRowStyle
                ? BuildHighlightedTextRenderable(plainText, [], Style.Parse(rowStyle), Style.Parse(matchStyle))
                : renderable;
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

        return hits.Count == 0 && !applyRowStyle
            ? renderable
            : BuildHighlightedTextRenderable(plainText, hits, Style.Parse(rowStyle), Style.Parse(matchStyle));
    }

    private static string ExtractRenderableText(IRenderable renderable) {
        try {
            var options = RenderOptions.Create(AnsiConsole.Console);
            IEnumerable<Segment> segments = renderable.Render(options, maxWidth: 200);
            var builder = new StringBuilder();

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

            string extracted = builder.ToString();
            if (!string.IsNullOrEmpty(extracted)) {
                return extracted;
            }
        }
        catch {
        }

        try {
            string rendered = Writer.WriteToString(renderable, width: 200);
            return string.IsNullOrEmpty(rendered)
                ? string.Empty
                : rendered;
        }
        catch {
            return string.Empty;
        }
    }

}
