namespace PSTextMate.Utilities;

/// <summary>
/// Simple pager implemented with Spectre.Console Live display.
/// Interaction keys:
/// - Up/Down: move one renderable item
/// - PageUp/PageDown: move by one viewport of items
/// - Home/End: go to start/end
/// - q or Escape: quit
/// </summary>
public sealed class Pager : IDisposable {
    private static readonly PagerExclusivityMode s_pagerExclusivityMode = new();
    private readonly IReadOnlyList<IRenderable> _renderables;
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
    private List<int> _renderableHeights = [];
    private bool _lastPageHadImages;
    private readonly record struct ViewportWindow(int Top, int Count, int EndExclusive, bool HasImages);

    private bool UseRichFooter(int footerWidth)
        => footerWidth >= GetMinimumRichFooterWidth();

    private int GetFooterHeight(int footerWidth)
        => UseRichFooter(footerWidth) ? 3 : 1;

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

    private static double GetTerminalCellAspectRatio() {
        CellSize cellSize = Compatibility.GetCellSize();
        return cellSize.PixelWidth <= 0 || cellSize.PixelHeight <= 0
            ? 0.5d
            : (double)cellSize.PixelWidth / cellSize.PixelHeight;
    }

    private static int EstimateImageHeight(IRenderable renderable, int width, int contentRows, RenderOptions options) {
        if (renderable is PixelImage pixelImage) {
            int imagePixelWidth = pixelImage.Width;
            int imagePixelHeight = pixelImage.Height;
            int cellWidth = pixelImage.MaxWidth is int maxWidth && maxWidth > 0
                ? Math.Min(width, maxWidth)
                : width;

            if (imagePixelWidth > 0 && imagePixelHeight > 0) {
                double imageAspect = (double)imagePixelHeight / imagePixelWidth;
                double cellAspectRatio = GetTerminalCellAspectRatio();
                int estimatedRows = (int)Math.Ceiling(imageAspect * Math.Max(1, cellWidth) * cellAspectRatio);
                return Math.Clamp(Math.Max(1, estimatedRows), 1, contentRows);
            }
        }

        Measurement measure;
        try {
            measure = renderable.Measure(options, width);
        }
        catch {
            return Math.Clamp(contentRows, 1, contentRows);
        }

        int cellWidthFallback = Math.Max(1, Math.Min(width, measure.Max));

        // Last fallback: keep as atomic item, but estimate from measured width.
        return Math.Clamp(Math.Max(1, (int)Math.Ceiling((double)cellWidthFallback / Math.Max(1, width))), 1, contentRows);
    }

    private bool IsMarkdownSource()
        => _sourceHighlightedText is not null
            && _sourceHighlightedText.Language.Contains("markdown", StringComparison.OrdinalIgnoreCase);

    private bool IsImageRenderable(IRenderable? renderable) {
        if (renderable is null) {
            return false;
        }

        if (_sourceHighlightedText is not null && !IsMarkdownSource()) {
            return false;
        }

        string name = renderable.GetType().Name;
        return name.Contains("Sixel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pixel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Image", StringComparison.OrdinalIgnoreCase);
    }

    private ViewportWindow BuildViewport(int proposedTop, int contentRows) {
        if (_renderables.Count == 0) {
            return new ViewportWindow(0, 0, 0, false);
        }

        int clampedTop = Math.Clamp(proposedTop, 0, _renderables.Count - 1);
        int rowsUsed = 0;
        int count = 0;
        bool hasImages = false;

        for (int i = clampedTop; i < _renderables.Count; i++) {
            bool isImage = IsImageRenderable(_renderables[i]);
            int height = Math.Clamp(GetRenderableHeight(i), 1, contentRows);

            if (count > 0 && rowsUsed + height > contentRows) {
                break;
            }

            rowsUsed += height;
            count++;
            hasImages |= isImage;

            if (rowsUsed >= contentRows) {
                break;
            }
        }

        if (count == 0) {
            count = 1;
            hasImages = IsImageRenderable(_renderables[clampedTop]);
        }

        return new ViewportWindow(clampedTop, count, clampedTop + count, hasImages);
    }

    public Pager(HighlightedText highlightedText) {
        _sourceHighlightedText = highlightedText;
        _originalWrapInPanel = highlightedText.WrapInPanel;

        int totalLines = highlightedText.LineCount;
        int lastLineNumber = highlightedText.LineNumberStart + Math.Max(0, totalLines - 1);
        _stableLineNumberWidth = highlightedText.LineNumberWidth ?? lastLineNumber.ToString(CultureInfo.InvariantCulture).Length;
        _originalLineNumberStart = highlightedText.LineNumberStart;
        _originalLineNumberWidth = highlightedText.LineNumberWidth;

        // Panel rendering in pager mode causes unstable layout; disable it for the paging session.
        highlightedText.WrapInPanel = false;

        // Reference the underlying renderable array directly to avoid copying.
        _renderables = highlightedText.Renderables;
        _statusColumnWidth = GetStatusColumnWidth(_renderables.Count);
        _top = 0;
    }

    public Pager(IEnumerable<IRenderable> renderables) {
        var list = renderables?.ToList();
        _renderables = list is null ? [] : (IReadOnlyList<IRenderable>)list;
        _statusColumnWidth = GetStatusColumnWidth(_renderables.Count);
        _top = 0;
    }
    private void Navigate(LiveDisplayContext ctx, bool useAlternateBuffer) {
        bool running = true;
        (WindowWidth, WindowHeight) = GetPagerSize();
        bool forceRedraw = true;

        while (running) {
            (int width, int pageHeight) = GetPagerSize();
            int footerHeight = GetFooterHeight(width);
            int contentRows = Math.Max(1, pageHeight - footerHeight);

            bool resized = width != WindowWidth || pageHeight != WindowHeight;
            if (resized) {
                AnsiConsole.Console.Profile.Width = width;

                WindowWidth = width;
                WindowHeight = pageHeight;
                forceRedraw = true;
            }

            // Redraw if needed (initial, resize, or after navigation)
            if (resized || forceRedraw) {
                VTHelpers.BeginSynchronizedOutput();
                try {
                    RecalculateRenderableHeights(width, contentRows);
                    _top = Math.Clamp(_top, 0, GetMaxTop(contentRows));
                    ViewportWindow viewport = BuildViewport(_top, contentRows);
                    _top = viewport.Top;

                    bool fullClear = resized || viewport.HasImages || _lastPageHadImages;
                    if (fullClear) {
                        VTHelpers.ClearScreen();
                    }
                    else {
                        VTHelpers.SetCursorPosition(1, 1);
                    }

                    IRenderable target = BuildRenderable(viewport, width);
                    ctx.UpdateTarget(target);
                    ctx.Refresh();

                    // Clear any stale lines after a terminal shrink.
                    if (_lastRenderedRows > pageHeight) {
                        for (int r = pageHeight + 1; r <= _lastRenderedRows; r++) {
                            VTHelpers.ClearRow(r);
                        }
                    }

                    _lastRenderedRows = pageHeight;
                    _lastPageHadImages = viewport.HasImages;
                    forceRedraw = false;
                }
                finally {
                    VTHelpers.EndSynchronizedOutput();
                }
            }

            // Wait for input, checking for resize while idle
            if (!Console.KeyAvailable) {
                Thread.Sleep(50);
                continue;
            }

            ConsoleKeyInfo key = Console.ReadKey(true);
            lock (_lock) {
                switch (key.Key) {
                    case ConsoleKey.DownArrow:
                        ScrollRenderable(1);
                        forceRedraw = true;
                        break;
                    case ConsoleKey.UpArrow:
                        ScrollRenderable(-1);
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
                    case ConsoleKey.Q:
                    case ConsoleKey.Escape:
                        running = false;
                        break;
                }
            }
        }
    }

    private static (int width, int height) GetPagerSize() {
        int width = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        int height = Console.WindowHeight > 0 ? Console.WindowHeight : 40;
        return (width, height);
    }

    private void ScrollRenderable(int delta) {
        if (_renderables.Count == 0) return;

        int direction = Math.Sign(delta);
        if (direction == 0) return;

        int maxTop = GetMaxTop(Math.Max(1, WindowHeight - 1));
        _top = Math.Clamp(_top + direction, 0, maxTop);
    }

    private void PageDown(int contentRows) {
        if (_renderables.Count == 0) return;

        ViewportWindow viewport = BuildViewport(_top, contentRows);
        int maxTop = GetMaxTop(contentRows);
        if (viewport.EndExclusive >= _renderables.Count) {
            _top = maxTop;
            return;
        }

        _top = Math.Min(viewport.EndExclusive, maxTop);
    }

    private void PageUp(int contentRows) {
        if (_renderables.Count == 0) return;

        int rowsSkipped = 0;
        int idx = _top - 1;
        int nextTop = _top;
        while (idx >= 0 && rowsSkipped < contentRows) {
            rowsSkipped += Math.Clamp(GetRenderableHeight(idx), 1, contentRows);
            nextTop = idx;
            idx--;
        }

        _top = Math.Clamp(nextTop, 0, _renderables.Count - 1);
    }

    private int GetRenderableHeight(int index)
        => index < 0 || index >= _renderableHeights.Count ? 1 : Math.Max(1, _renderableHeights[index]);

    private static int CountLinesSegments(List<Segment> segments) {
        if (segments.Count == 0) {
            return 0;
        }

        int lineBreaks = segments.Count(segment => segment.IsLineBreak);
        return lineBreaks == 0 ? 1 : segments[^1].IsLineBreak ? lineBreaks : lineBreaks + 1;
    }

    private void RecalculateRenderableHeights(int width, int contentRows) {
        _renderableHeights = new List<int>(_renderables.Count);
        Capabilities capabilities = AnsiConsole.Console.Profile.Capabilities;
        var size = new Size(width, Math.Max(1, Console.WindowHeight));
        var options = new RenderOptions(capabilities, size);

        for (int i = 0; i < _renderables.Count; i++) {
            IRenderable? r = _renderables[i];
            if (r is null) {
                _renderableHeights.Add(1);
                continue;
            }

            if (IsImageRenderable(r)) {
                if (r is PixelImage pixelImage) {
                    // In pager mode, clamp image width to the viewport so frames stay within screen bounds.
                    pixelImage.MaxWidth = pixelImage.MaxWidth is int existingWidth && existingWidth > 0
                        ? Math.Min(existingWidth, width)
                        : width;
                }

                _renderableHeights.Add(EstimateImageHeight(r, width, contentRows, options));
                continue;
            }

            try {
                // For non-image renderables, render to segments to get accurate row count.
                // This avoids overflow/cropping artifacts when wrapped text spans many rows.
                var segments = r.Render(options, width).ToList();
                int lines = CountLinesSegments(segments);
                _renderableHeights.Add(Math.Max(1, lines));
            }
            catch {
                // Fallback: assume single-line if measurement fails
                _renderableHeights.Add(1);
            }
        }
    }

    private void GoToTop() => _top = 0;

    private int GetMaxTop(int contentRows) {
        if (_renderables.Count == 0) {
            return 0;
        }

        int top = _renderables.Count - 1;
        int rows = Math.Clamp(GetRenderableHeight(top), 1, contentRows);

        while (top > 0) {
            int previousHeight = Math.Clamp(GetRenderableHeight(top - 1), 1, contentRows);
            if (rows + previousHeight > contentRows) {
                break;
            }

            rows += previousHeight;
            top--;
        }

        return top;
    }

    private void GoToEnd(int contentRows) => _top = GetMaxTop(contentRows);

    private Layout BuildRenderable(ViewportWindow viewport, int width) {
        int footerHeight = GetFooterHeight(width);
        IRenderable content = viewport.Count <= 0
            ? Text.Empty
            : BuildContentRenderable(viewport);

        IRenderable footer = BuildFooter(width, viewport);
        var root = new Layout("root");
        root.SplitRows(
            new Layout("body").Update(content),
            new Layout("footer").Size(footerHeight).Update(footer)
        );

        return root;
    }

    private IRenderable BuildContentRenderable(ViewportWindow viewport) {
        if (_sourceHighlightedText is not null) {
            _sourceHighlightedText.SetView(_renderables, viewport.Top, viewport.Count);
            _sourceHighlightedText.LineNumberStart = (_originalLineNumberStart ?? 1) + viewport.Top;
            _sourceHighlightedText.LineNumberWidth = _stableLineNumberWidth;
            return _sourceHighlightedText;
        }

        return new Rows(_renderables.Skip(viewport.Top).Take(viewport.Count));
    }

    private IRenderable BuildFooter(int width, ViewportWindow viewport)
        => UseRichFooter(width)
            ? BuildRichFooter(width, viewport)
            : BuildSimpleFooter(viewport);

    private Text BuildSimpleFooter(ViewportWindow viewport) {
        int total = _renderables.Count;
        int start = total == 0 ? 0 : viewport.Top + 1;
        int end = viewport.EndExclusive;
        return new Text($"↑↓ Scroll  PgUp/PgDn Page  Home/End Jump  q/Esc Quit    {start}-{end}/{total}", new Style(Color.Grey));
    }

    private Panel BuildRichFooter(int width, ViewportWindow viewport) {
        int total = _renderables.Count;
        int start = total == 0 ? 0 : viewport.Top + 1;
        int end = viewport.EndExclusive;
        int safeTotal = Math.Max(1, total);
        int digits = Math.Max(1, safeTotal.ToString(CultureInfo.InvariantCulture).Length);

        string keyText = "↑↓ Scroll  PgUp/PgDn Page  Home/End Jump  q/Esc Quit";
        string statusText = $"{start.ToString(CultureInfo.InvariantCulture).PadLeft(digits)}-{end.ToString(CultureInfo.InvariantCulture).PadLeft(digits)}/{total.ToString(CultureInfo.InvariantCulture).PadLeft(digits)}".PadLeft(_statusColumnWidth);

        int chartWidth = Math.Clamp(width / 5, 12, 28);
        double progressUnits = total == 0 ? 0d : (double)end / safeTotal * chartWidth;
        double chartValue = end <= 0 ? 0d : Math.Clamp(Math.Ceiling(progressUnits), Math.Min(4d, chartWidth), chartWidth);
        BarChart chart = new BarChart()
            .Width(chartWidth)
            .WithMaxValue(chartWidth)
            .HideValues()
            .AddItem(" ", chartValue, Color.Lime);

        Columns columns = new([
            new Text(keyText, new Style(Color.Grey)),
            new Markup($"[bold]{statusText}[/]"),
            chart
        ]) {
            Expand = true,
            Padding = new Padding(2, 0, 2, 0)
        };

        return new Panel(columns) {
            Border = BoxBorder.Rounded,
            Padding = new Padding(0, 0, 0, 0),
            Expand = true
        };
    }

    public void Show() {
        bool resolvedUseAlternateBuffer = VTHelpers.SupportsAlternateBuffer();

        s_pagerExclusivityMode.Run(() => {
            if (resolvedUseAlternateBuffer) {
                AnsiConsole.Console.AlternateScreen(() => ShowCore(useAlternateBuffer: true));
            }
            else {
                ShowCore(useAlternateBuffer: false);
            }

            return 0;
        });
    }

    private void ShowCore(bool useAlternateBuffer) {
        VTHelpers.HideCursor();
        try {
            (int width, int pageHeight) = GetPagerSize();
            int footerHeight = GetFooterHeight(width);
            int contentRows = Math.Max(1, pageHeight - footerHeight);
            WindowWidth = width;
            WindowHeight = pageHeight;

            // Initial target for Spectre Live (footer included in target renderable)
            AnsiConsole.Console.Profile.Width = width;
            RecalculateRenderableHeights(width, contentRows);
            ViewportWindow initialViewport = BuildViewport(_top, contentRows);
            _top = initialViewport.Top;
            IRenderable initial = BuildRenderable(initialViewport, width);
            _lastRenderedRows = pageHeight;
            _lastPageHadImages = initialViewport.HasImages;

            // If the initial page contains images, clear appropriately to ensure safe image rendering
            if (initialViewport.HasImages) {
                VTHelpers.BeginSynchronizedOutput();
                try {
                    if (useAlternateBuffer) {
                        VTHelpers.ClearScreen();
                    }
                    else {
                        VTHelpers.ClearScreen();
                    }
                }
                finally {
                    VTHelpers.EndSynchronizedOutput();
                }
            }

            AnsiConsole.Live(initial)
            .AutoClear(true)
            .Overflow(VerticalOverflow.Crop)
            .Cropping(VerticalOverflowCropping.Bottom)
            .Start(ctx => {
                // Enter interactive loop using the live display context
                Navigate(ctx, useAlternateBuffer);
            });
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
            // Reset scroll region and restore normal screen buffer if used
            if (useAlternateBuffer) {
                VTHelpers.ResetScrollRegion();
            }
            VTHelpers.ShowCursor();
        }
    }

    public void Dispose() {
        // No resources to dispose, but required for IDisposable
    }
}
