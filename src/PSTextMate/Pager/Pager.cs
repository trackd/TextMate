namespace PSTextMate.Terminal;

/// <summary>
/// Simple pager implemented with Spectre.Console Live display.
/// Interaction keys:
/// - Up/Down: move one renderable item
/// - PageUp/PageDown: move by one viewport of items
/// - Home/End: go to start/end
/// - / or Ctrl+F: prompt for search query
/// - n / N: next / previous match
/// - c: clear active search
/// - ?: show/hide keybindings
/// - q or Escape: quit
/// </summary>
public sealed class Pager {
    private static readonly PagerExclusivityMode s_pagerExclusivityMode = new();
    private readonly IAnsiConsole _console;
    private readonly Func<ConsoleKeyInfo?>? _tryReadKeyOverride;
    private readonly bool _suppressTerminalControlSequences;
    private readonly IReadOnlyList<IRenderable> _renderables;
    private readonly PagerDocument _document;
    private readonly PagerSearchSession _search;
    private readonly PagerViewportEngine _viewportEngine;
    private readonly HighlightedText? _sourceHighlightedText;
    private readonly int? _originalLineNumberStart;
    private readonly int? _originalLineNumberWidth;
    private readonly bool? _originalWrapInPanel;
    private readonly int? _stableLineNumberWidth;
    private readonly int _statusColumnWidth;
    private int _top;
    private int WindowHeight;
    private int WindowWidth;
    private readonly object _lock = new();
    private int _lastRenderedRows;
    private bool _lastPageHadImages;
    private string _searchStatusText = string.Empty;
    private bool _isSearchInputActive;
    private bool _isHelpOverlayActive;
    private readonly StringBuilder _searchInputBuffer = new(64);
    private static readonly Style SearchRowTextStyle = new(Color.White, Color.Grey);
    private static readonly Style SearchMatchTextStyle = new(Color.Black, Color.Orange1);
    private const string SearchRowStyle = "white on grey";
    private const string SearchMatchStyle = "black on orange1";

    private bool TryReadKey(out ConsoleKeyInfo key) {
        if (_tryReadKeyOverride is not null) {
            ConsoleKeyInfo? injected = _tryReadKeyOverride();
            if (injected.HasValue) {
                key = injected.Value;
                return true;
            }

            key = default;
            return false;
        }

        return TryReadKeyFromConsole(out key);
    }

    private static bool TryReadKeyFromConsole(out ConsoleKeyInfo key) {
        try {
            if (!Console.KeyAvailable) {
                key = default;
                return false;
            }

            key = Console.ReadKey(true);
            return true;
        }
        catch (IOException) {
            key = default;
            return false;
        }
        catch (InvalidOperationException) {
            key = default;
            return false;
        }
    }

    private bool UseRichFooter(int footerWidth)
        => footerWidth >= GetMinimumRichFooterWidth();

    private int GetFooterHeight(int footerWidth)
        => UseRichFooter(footerWidth) ? 3 : 1;

    private int GetSearchInputHeight()
        => _isSearchInputActive ? 3 : 0;

    private int GetMinimumRichFooterWidth() {
        const int keySectionMinWidth = 38;
        const int chartSectionMinWidth = 12;
        const int layoutOverhead = 10;
        return keySectionMinWidth + _statusColumnWidth + chartSectionMinWidth + layoutOverhead;
    }

    private static int GetStatusColumnWidth(int totalItems) {
        int digits = Math.Max(1, totalItems.ToString(CultureInfo.InvariantCulture).Length);
        return (digits * 3) + 4;
    }

    private sealed class PagerExclusivityMode : IExclusivityMode {
        private readonly object _syncRoot = new();

        public T Run<T>(Func<T> func) {
            ArgumentNullException.ThrowIfNull(func);

            lock (_syncRoot) {
                return func();
            }
        }

        public async Task<T> RunAsync<T>(Func<Task<T>> func) {
            ArgumentNullException.ThrowIfNull(func);

            Task<T> task;
            lock (_syncRoot) {
                task = func();
            }
            return await task.ConfigureAwait(false);
        }
    }


    public Pager(HighlightedText highlightedText) {
        _console = AnsiConsole.Console;
        _tryReadKeyOverride = null;
        _suppressTerminalControlSequences = false;
        _sourceHighlightedText = highlightedText;
        _originalWrapInPanel = highlightedText.WrapInPanel;

        int totalLines = highlightedText.LineCount;
        int lastLineNumber = highlightedText.LineNumberStart + Math.Max(0, totalLines - 1);
        _stableLineNumberWidth = highlightedText.LineNumberWidth ?? lastLineNumber.ToString(CultureInfo.InvariantCulture).Length;
        _originalLineNumberStart = highlightedText.LineNumberStart;
        _originalLineNumberWidth = highlightedText.LineNumberWidth;

        // Panel rendering in pager mode causes unstable layout; disable it for the paging session.
        highlightedText.WrapInPanel = false;

        _document = PagerDocument.FromHighlightedText(highlightedText);
        _renderables = _document.Renderables;
        _search = new PagerSearchSession(_document);
        _viewportEngine = new PagerViewportEngine(_renderables, _sourceHighlightedText);
        _statusColumnWidth = GetStatusColumnWidth(_renderables.Count);
        _top = 0;
    }

    public Pager(IEnumerable<IRenderable> renderables)
        : this(renderables, AnsiConsole.Console, null, suppressTerminalControlSequences: false) {
    }

    internal Pager(
        IEnumerable<IRenderable> renderables,
        IAnsiConsole console,
        Func<ConsoleKeyInfo?>? tryReadKeyOverride = null,
        bool suppressTerminalControlSequences = false
    ) {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _tryReadKeyOverride = tryReadKeyOverride;
        _suppressTerminalControlSequences = suppressTerminalControlSequences;
        _document = new PagerDocument(renderables ?? []);
        _renderables = _document.Renderables;
        _search = new PagerSearchSession(_document);
        _viewportEngine = new PagerViewportEngine(_renderables, _sourceHighlightedText);
        _statusColumnWidth = GetStatusColumnWidth(_renderables.Count);
        _top = 0;
    }
    private void Navigate(LiveDisplayContext ctx) {
        bool running = true;
        (WindowWidth, WindowHeight) = GetPagerSize();
        bool forceRedraw = true;

        while (running) {
            (int width, int pageHeight) = GetPagerSize();
            int footerHeight = GetFooterHeight(width);
            int searchInputHeight = GetSearchInputHeight();
            int contentRows = Math.Max(1, pageHeight - footerHeight - searchInputHeight);

            bool resized = width != WindowWidth || pageHeight != WindowHeight;
            if (resized) {
                _console.Profile.Width = width;

                WindowWidth = width;
                WindowHeight = pageHeight;
                forceRedraw = true;
            }

            // Redraw if needed (initial, resize, or after navigation)
            if (resized || forceRedraw) {
                if (!_suppressTerminalControlSequences) {
                    VTHelpers.BeginSynchronizedOutput();
                }

                try {
                    _viewportEngine.RecalculateHeights(width, contentRows, WindowHeight, _console);
                    _top = Math.Clamp(_top, 0, _viewportEngine.GetMaxTop(contentRows));
                    PagerViewportWindow viewport = _viewportEngine.BuildViewport(_top, contentRows);
                    _top = viewport.Top;

                    bool fullClear = resized || viewport.HasImages || _lastPageHadImages;
                    if (!_suppressTerminalControlSequences) {
                        if (fullClear) {
                            VTHelpers.ClearScreen();
                        }
                        else {
                            VTHelpers.SetCursorPosition(1, 1);
                        }
                    }

                    IRenderable target = BuildRenderable(viewport, width);
                    ctx.UpdateTarget(target);
                    ctx.Refresh();

                    // Clear any stale lines after a terminal shrink.
                    if (!_suppressTerminalControlSequences && _lastRenderedRows > pageHeight) {
                        for (int r = pageHeight + 1; r <= _lastRenderedRows; r++) {
                            VTHelpers.ClearRow(r);
                        }
                    }

                    _lastRenderedRows = pageHeight;
                    _lastPageHadImages = viewport.HasImages;
                    forceRedraw = false;
                }
                finally {
                    if (!_suppressTerminalControlSequences) {
                        VTHelpers.EndSynchronizedOutput();
                    }
                }
            }

            // Wait for input, checking for resize while idle.
            if (!TryReadKey(out ConsoleKeyInfo key)) {
                Thread.Sleep(50);
                continue;
            }

            lock (_lock) {
                if (_isSearchInputActive) {
                    HandleSearchInputKey(key, ref forceRedraw);
                    continue;
                }

                if (_isHelpOverlayActive) {
                    if (key.Key == ConsoleKey.Q) {
                        running = false;
                        continue;
                    }

                    _isHelpOverlayActive = false;
                    forceRedraw = true;

                    if (key.Key == ConsoleKey.Escape || key.KeyChar == '?') {
                        continue;
                    }

                    continue;
                }

                bool isCtrlF = key.Key == ConsoleKey.F && (key.Modifiers & ConsoleModifiers.Control) != 0;
                if (key.KeyChar == '/' || isCtrlF) {
                    BeginSearchInput();
                    forceRedraw = true;
                    continue;
                }

                if (key.KeyChar == '?') {
                    _isHelpOverlayActive = true;
                    forceRedraw = true;
                    continue;
                }

                switch (key.Key) {
                    case ConsoleKey.DownArrow:
                        ScrollRenderable(1, contentRows);
                        forceRedraw = true;
                        break;
                    case ConsoleKey.UpArrow:
                        ScrollRenderable(-1, contentRows);
                        forceRedraw = true;
                        break;
                    case ConsoleKey.Spacebar:
                    case ConsoleKey.PageDown:
                        PageDown(contentRows);
                        forceRedraw = true;
                        break;
                    case ConsoleKey.PageUp:
                        PageUp(contentRows);
                        forceRedraw = true;
                        break;
                    case ConsoleKey.Home:
                        GoToTop();
                        forceRedraw = true;
                        break;
                    case ConsoleKey.End:
                        GoToEnd(contentRows);
                        forceRedraw = true;
                        break;
                    case ConsoleKey.N:
                        if ((key.Modifiers & ConsoleModifiers.Shift) != 0) {
                            JumpToPreviousMatch();
                        }
                        else {
                            JumpToNextMatch();
                        }

                        forceRedraw = true;
                        break;
                    case ConsoleKey.C:
                        if (_search.HasQuery) {
                            ClearSearch();
                            forceRedraw = true;
                        }

                        break;
                    case ConsoleKey.Q:
                    case ConsoleKey.Escape:
                        running = false;
                        break;
                }
            }
        }
    }

    private static (int width, int height) GetPagerSize() {
        try {
            int width = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
            int height = Console.WindowHeight > 0 ? Console.WindowHeight : 40;
            return (width, height);
        }
        catch (IOException) {
            return (80, 40);
        }
        catch (InvalidOperationException) {
            return (80, 40);
        }
    }

    private void ScrollRenderable(int delta, int contentRows) => _top = _viewportEngine.ScrollTop(_top, delta, contentRows);

    private void PageDown(int contentRows) => _top = _viewportEngine.PageDownTop(_top, contentRows);

    private void PageUp(int contentRows) => _top = _viewportEngine.PageUpTop(_top, contentRows);

    private void GoToTop() => _top = 0;

    private void BeginSearchInput() {
        _isSearchInputActive = true;
        _searchInputBuffer.Clear();
        if (!string.IsNullOrEmpty(_search.Query)) {
            _searchInputBuffer.Append(_search.Query);
        }
    }

    private void HandleSearchInputKey(ConsoleKeyInfo key, ref bool forceRedraw) {
        switch (key.Key) {
            case ConsoleKey.Enter:
                _isSearchInputActive = false;
                ApplySearchQuery(_searchInputBuffer.ToString());
                forceRedraw = true;
                return;
            case ConsoleKey.Escape:
                _isSearchInputActive = false;
                forceRedraw = true;
                return;
            case ConsoleKey.Backspace:
                if (_searchInputBuffer.Length > 0) {
                    _searchInputBuffer.Length--;
                    forceRedraw = true;
                }

                return;
        }

        if (!char.IsControl(key.KeyChar)) {
            _searchInputBuffer.Append(key.KeyChar);
            forceRedraw = true;
        }
    }

    private void ApplySearchQuery(string query) {
        _search.SetQuery(query);
        if (!_search.HasQuery) {
            _searchStatusText = string.Empty;
            return;
        }

        PagerSearchHit? hit = _search.MoveNext(_top);
        if (hit is null) {
            _searchStatusText = $"/{_search.Query} (no matches)";
            return;
        }

        _top = hit.RenderableIndex;
        _searchStatusText = BuildSearchStatus();
    }

    private void ClearSearch() {
        _search.SetQuery(string.Empty);
        _searchInputBuffer.Clear();
        _searchStatusText = string.Empty;
    }

    private void JumpToNextMatch() {
        if (!_search.HasQuery) {
            _searchStatusText = "No active search. Press / to search.";
            return;
        }

        PagerSearchHit? hit = _search.MoveNext(_top);
        if (hit is null) {
            _searchStatusText = $"/{_search.Query} (no matches)";
            return;
        }

        _top = hit.RenderableIndex;
        _searchStatusText = BuildSearchStatus();
    }

    private void JumpToPreviousMatch() {
        if (!_search.HasQuery) {
            _searchStatusText = "No active search. Press / to search.";
            return;
        }

        PagerSearchHit? hit = _search.MovePrevious(_top);
        if (hit is null) {
            _searchStatusText = $"/{_search.Query} (no matches)";
            return;
        }

        _top = hit.RenderableIndex;
        _searchStatusText = BuildSearchStatus();
    }

    private string BuildSearchStatus() {
        PagerSearchHit? hit = _search.CurrentHit;
        if (hit is null) {
            return $"/{_search.Query} (0 matches)";
        }

        int current = _search.CurrentHitIndex + 1;
        int line = hit.Line + 1;
        int column = hit.Column + 1;
        return $"/{_search.Query} [{current}/{_search.HitCount}] line {line}, col {column}";
    }

    private void GoToEnd(int contentRows) => _top = _viewportEngine.GetMaxTop(contentRows);

    private Layout BuildRenderable(PagerViewportWindow viewport, int width) {
        int footerHeight = GetFooterHeight(width);
        int searchInputHeight = GetSearchInputHeight();
        IRenderable content = _isHelpOverlayActive
            ? BuildHelpOverlayPanel()
            : viewport.Count <= 0
                ? Text.Empty
                : BuildContentRenderable(viewport);

        IRenderable footer = BuildFooter(width, viewport);
        var root = new Layout("root");
        Layout bodyLayout = new Layout("body").Ratio(1).Update(content);
        if (_isSearchInputActive) {
            root.SplitRows(
                new Layout("search").Size(searchInputHeight).Update(BuildSearchInputPanel()),
                bodyLayout,
                new Layout("footer").Size(footerHeight).Update(footer)
            );
        }
        else {
            root.SplitRows(
                bodyLayout,
                new Layout("footer").Size(footerHeight).Update(footer)
            );
        }

        return root;
    }

    private Panel BuildSearchInputPanel() {
        string inputText = Markup.Escape(_searchInputBuffer.ToString());
        string prompt = $"[bold]/[/]{inputText}[grey]_[/]";
        var content = new Markup(prompt);
        return new Panel(content) {
            Header = new PanelHeader("Search", Justify.Left),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
    }

    private static Panel BuildHelpOverlayPanel() {
        var helpRows = new Rows(
            new Text("Keybindings", new Style(Color.White, decoration: Decoration.Bold)),
            Text.Empty,
            new Text("  Up/Down       Move one item", new Style(Color.Grey)),
            new Text("  PgUp/PgDn     Page navigation", new Style(Color.Grey)),
            new Text("  Home/End      Jump to start/end", new Style(Color.Grey)),
            new Text("  / or Ctrl+F   Search", new Style(Color.Grey)),
            new Text("  n / N         Next / previous match", new Style(Color.Grey)),
            new Text("  c             Clear active search", new Style(Color.Grey)),
            new Text("  ?             Toggle this help", new Style(Color.Grey)),
            new Text("  q or Esc      Quit pager", new Style(Color.Grey)),
            Text.Empty,
            new Text("Press ? or Esc to close help.", new Style(Color.Yellow))
        );

        return new Panel(new Align(helpRows, HorizontalAlignment.Left, VerticalAlignment.Middle)) {
            Header = new PanelHeader("Pager Help", Justify.Left),
            Border = BoxBorder.Double,
            Padding = new Padding(2, 1, 2, 1),
            Expand = true
        };
    }

    private IRenderable BuildContentRenderable(PagerViewportWindow viewport) {
        if (_sourceHighlightedText is not null) {
            if (_search.HasQuery) {
                List<IRenderable> highlightedItems = BuildSearchAwareItems(viewport);
                _sourceHighlightedText.SetView(highlightedItems, 0, highlightedItems.Count);
            }
            else {
                _sourceHighlightedText.SetView(_renderables, viewport.Top, viewport.Count);
            }

            _sourceHighlightedText.LineNumberStart = (_originalLineNumberStart ?? 1) + viewport.Top;
            _sourceHighlightedText.LineNumberWidth = _stableLineNumberWidth;
            return _sourceHighlightedText;
        }

        return _search.HasQuery ? BuildSearchAwareContent(viewport) : new Rows(_renderables.Skip(viewport.Top).Take(viewport.Count));
    }

    private List<IRenderable> BuildSearchAwareItems(PagerViewportWindow viewport) {
        var items = new List<IRenderable>(viewport.Count);
        for (int i = 0; i < viewport.Count; i++) {
            int renderableIndex = viewport.Top + i;
            IRenderable highlighted = ApplySearchHighlight(renderableIndex, _renderables[renderableIndex]);
            items.Add(highlighted);
        }

        return items;
    }

    private Rows BuildSearchAwareContent(PagerViewportWindow viewport)
        => new(BuildSearchAwareItems(viewport));

    private IRenderable ApplySearchHighlight(int renderableIndex, IRenderable renderable) {
        IReadOnlyList<PagerSearchHit> hits = _search.GetHitsForRenderable(renderableIndex);

        bool isStructuredRenderable = PagerHighlighting.IsStructuredRowHighlightCandidate(renderable);

        // Keep table-specific highlighting in the structured path so row styling
        // is scoped to matching rows rather than the whole rendered table block.
        if ((isStructuredRenderable || hits.Count == 0)
            && PagerHighlighting.TryBuildStructuredHighlightRenderable(
                renderable,
                _search.Query,
                SearchRowStyle,
                SearchMatchStyle,
                hits,
                out IRenderable structuredHighlight
            )) {
            return structuredHighlight;
        }

        string plainText = GetSearchTextForHighlight(renderableIndex, renderable);
        if (plainText.Length == 0 || !_search.HasQuery) {
            return renderable;
        }

        if (hits.Count == 0) {
            hits = BuildQueryHits(plainText, _search.Query, renderableIndex);
            if (hits.Count == 0) {
                return renderable;
            }
        }

        return PagerHighlighting.BuildSegmentHighlightRenderable(renderable, _search.Query, SearchRowTextStyle, SearchMatchTextStyle);
    }

    private static List<PagerSearchHit> BuildQueryHits(string plainText, string query, int renderableIndex) {
        if (string.IsNullOrEmpty(plainText) || string.IsNullOrWhiteSpace(query)) {
            return [];
        }

        string normalizedQuery = query.Trim();
        if (normalizedQuery.Length == 0) {
            return [];
        }

        var hits = new List<PagerSearchHit>();
        int searchStart = 0;
        while (searchStart <= plainText.Length - normalizedQuery.Length) {
            int hitOffset = plainText.IndexOf(normalizedQuery, searchStart, StringComparison.OrdinalIgnoreCase);
            if (hitOffset < 0) {
                break;
            }

            hits.Add(new PagerSearchHit(renderableIndex, hitOffset, normalizedQuery.Length, 0, hitOffset));
            searchStart = hitOffset + Math.Max(1, normalizedQuery.Length);
        }

        return hits;
    }

    private string GetSearchTextForHighlight(int renderableIndex, IRenderable renderable) {
        string normalizedEntryText = PagerHighlighting.NormalizeText(_document.GetEntry(renderableIndex)?.SearchText);
        return normalizedEntryText.Length > 0
            ? normalizedEntryText
            : ExtractPlainTextForSearchHighlight(renderable);
    }

    private string ExtractPlainTextForSearchHighlight(IRenderable renderable) {
        if (renderable is Text text) {
            return PagerHighlighting.NormalizeText(text.ToString());
        }

        try {
            int width = Math.Max(20, WindowWidth - 2);
            string rendered = Writer.WriteToString(renderable, width);
            string normalized = PagerHighlighting.NormalizeText(VTHelpers.StripAnsi(rendered));
            return normalized.Length > 0
                ? normalized
                : PagerHighlighting.NormalizeText(renderable.ToString());
        }
        catch {
            return PagerHighlighting.NormalizeText(renderable.ToString());
        }
    }

    private IRenderable BuildFooter(int width, PagerViewportWindow viewport)
        => UseRichFooter(width)
            ? BuildRichFooter(width, viewport)
            : BuildSimpleFooter(viewport);

    private Text BuildSimpleFooter(PagerViewportWindow viewport) {
        int total = _renderables.Count;
        int start = total == 0 ? 0 : viewport.Top + 1;
        int end = viewport.EndExclusive;
        string baseText = $"{start}-{end}/{total}";
        string defaultHelp = !_isSearchInputActive && string.IsNullOrEmpty(_searchStatusText)
            ? "   Press ? for help"
            : string.Empty;
        string inputHelp = _isSearchInputActive ? "   Search: Enter Apply  Esc Cancel" : string.Empty;
        return string.IsNullOrEmpty(_searchStatusText)
            ? new Text(baseText + defaultHelp + inputHelp, new Style(Color.Grey))
            : new Text($"{baseText}{inputHelp}   {_searchStatusText}", new Style(Color.Grey));
    }

    private Panel BuildRichFooter(int width, PagerViewportWindow viewport) {
        int total = _renderables.Count;
        int start = total == 0 ? 0 : viewport.Top + 1;
        int end = viewport.EndExclusive;
        int safeTotal = Math.Max(1, total);
        int digits = Math.Max(1, safeTotal.ToString(CultureInfo.InvariantCulture).Length);

        string keyText = "Press ? for help";
        string statusText = $"{start.ToString(CultureInfo.InvariantCulture).PadLeft(digits)}-{end.ToString(CultureInfo.InvariantCulture).PadLeft(digits)}/{total.ToString(CultureInfo.InvariantCulture).PadLeft(digits)}".PadLeft(_statusColumnWidth);
        if (_isSearchInputActive) {
            keyText = "Search: Enter Apply  Esc Cancel";
        }

        if (!string.IsNullOrEmpty(_searchStatusText)) {
            keyText = string.IsNullOrEmpty(keyText)
                ? _searchStatusText
                : $"{keyText}  {_searchStatusText}";
        }

        int chartWidth = Math.Clamp(width / 4, 14, 40);
        double progressUnits = total == 0 ? 0d : (double)end / safeTotal * chartWidth;
        double chartValue = end <= 0 ? 0d : Math.Clamp(Math.Ceiling(progressUnits), Math.Min(4d, chartWidth), chartWidth);
        BarChart chart = new BarChart()
            .Width(chartWidth)
            .WithMaxValue(chartWidth)
            .HideValues()
            .AddItem(" ", chartValue, Color.Lime);

        var footerBody = new Layout("footer-body");
        footerBody.SplitColumns(
            new Layout("keys").Ratio(1).Update(new Text(keyText, new Style(Color.Grey))),
            new Layout("status").Size(_statusColumnWidth).Update(new Align(new Markup($"[bold]{statusText}[/]"), HorizontalAlignment.Right)),
            new Layout("chart").Size(chartWidth).Update(chart)
        );

        return new Panel(footerBody) {
            Border = BoxBorder.Rounded,
            Padding = new Padding(0, 0, 0, 0),
            Expand = true
        };
    }

    public void Show() {
        s_pagerExclusivityMode.Run(() => {
            if (_suppressTerminalControlSequences) {
                ShowCore();
                return 0;
            }

            try {
                _console.AlternateScreen(ShowCore);
            }
            catch (NotSupportedException) {
                // Some hosts report no alternate-buffer/ANSI capability.
                // Keep pager functional by running on the main screen.
                ShowCore();
            }
            catch (IOException) {
                // Certain PTY hosts report invalid console handles for alternate screen.
                // Fall back to normal screen rendering so pager still works.
                ShowCore();
            }
            catch (InvalidOperationException) {
                // Console state can be partially unavailable in test/PTY environments.
                ShowCore();
            }

            return 0;
        });
    }

    private void ShowCore() {
        if (!_suppressTerminalControlSequences) {
            VTHelpers.HideCursor();
            VTHelpers.EnableAlternateScroll();
        }

        try {
            (int width, int pageHeight) = GetPagerSize();
            int footerHeight = GetFooterHeight(width);
            int searchInputHeight = GetSearchInputHeight();
            int contentRows = Math.Max(1, pageHeight - footerHeight - searchInputHeight);
            WindowWidth = width;
            WindowHeight = pageHeight;

            // Initial target for Spectre Live (footer included in target renderable)
            _console.Profile.Width = width;
            _viewportEngine.RecalculateHeights(width, contentRows, WindowHeight, _console);
            PagerViewportWindow initialViewport = _viewportEngine.BuildViewport(_top, contentRows);
            _top = initialViewport.Top;
            IRenderable initial = BuildRenderable(initialViewport, width);
            _lastRenderedRows = pageHeight;
            _lastPageHadImages = initialViewport.HasImages;

            // If the initial page contains images, clear appropriately to ensure safe image rendering
            if (initialViewport.HasImages) {
                if (!_suppressTerminalControlSequences) {
                    VTHelpers.BeginSynchronizedOutput();
                }

                try {
                    if (!_suppressTerminalControlSequences) {
                        VTHelpers.ClearScreen();
                    }
                }
                finally {
                    if (!_suppressTerminalControlSequences) {
                        VTHelpers.EndSynchronizedOutput();
                    }
                }
            }
            // Enter interactive loop using the live display context
            _console.Live(initial)
            .AutoClear(true)
            .Overflow(VerticalOverflow.Crop)
            .Cropping(VerticalOverflowCropping.Bottom)
            .Start(Navigate);
        }
        finally {
            // Clear any active view on the source highlighted text to avoid
            // leaving its state mutated after the pager exits, and restore
            // original line-number settings.
            if (_sourceHighlightedText is not null) {
                _sourceHighlightedText.ClearView();
                _sourceHighlightedText.LineNumberStart = _originalLineNumberStart ?? 1;
                _sourceHighlightedText.LineNumberWidth = _originalLineNumberWidth;
                _sourceHighlightedText.WrapInPanel = _originalWrapInPanel ?? false;
            }

            if (!_suppressTerminalControlSequences) {
                VTHelpers.DisableAlternateScroll();
                VTHelpers.ShowCursor();
            }
        }
    }

}
