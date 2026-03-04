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
    private readonly IReadOnlyList<IRenderable> _renderables;
    private readonly HighlightedText? _sourceHighlightedText;
    private readonly int? _originalLineNumberStart;
    private readonly int? _originalLineNumberWidth;
    private readonly int? _stableLineNumberWidth;
    private int _top;
    private int WindowHeight;
    private int WindowWidth;
    private readonly object _lock = new();
    private int _lastRenderedRows;
    private List<int> _renderableHeights = [];
    private bool _lastPageHadImages;

    private readonly record struct ViewportWindow(int Top, int Count, int EndExclusive, bool HasImages);

    private bool ContainsImageRenderables() => _renderables.Any(IsImageRenderable);

    private static int? GetIntPropertyValue(object instance, string propertyName) {
        PropertyInfo? property = instance.GetType().GetProperty(propertyName);
        if (property is null || !property.CanRead) {
            return null;
        }

        object? value = property.GetValue(instance);
        return value is int i ? i : null;
    }

    private static double GetTerminalCellAspectRatio() {
        try {
            var compatibility = Type.GetType("PwshSpectreConsole.Terminal.Compatibility, PwshSpectreConsole");
            MethodInfo? getCellSize = compatibility?.GetMethod("GetCellSize", Type.EmptyTypes);
            object? cellSize = getCellSize?.Invoke(null, null);
            if (cellSize is null) {
                return 0.5d;
            }

            PropertyInfo? pixelWidthProperty = cellSize.GetType().GetProperty("PixelWidth");
            PropertyInfo? pixelHeightProperty = cellSize.GetType().GetProperty("PixelHeight");
            int pixelWidth = (int?)pixelWidthProperty?.GetValue(cellSize) ?? 0;
            int pixelHeight = (int?)pixelHeightProperty?.GetValue(cellSize) ?? 0;
            return pixelWidth <= 0 || pixelHeight <= 0 ? 0.5d : (double)pixelWidth / pixelHeight;
        }
        catch {
            return 0.5d;
        }
    }

    private static int EstimateImageHeight(IRenderable renderable, int width, int contentRows, RenderOptions options) {
        // If an explicit max height exists, it is the strongest signal.
        int? explicitMaxHeight = GetIntPropertyValue(renderable, "MaxHeight");
        if (explicitMaxHeight.HasValue && explicitMaxHeight.Value > 0) {
            return Math.Clamp(explicitMaxHeight.Value, 1, contentRows);
        }

        int? imagePixelWidth = GetIntPropertyValue(renderable, "Width");
        int? imagePixelHeight = GetIntPropertyValue(renderable, "Height");

        Measurement measure;
        try {
            measure = renderable.Measure(options, width);
        }
        catch {
            return Math.Clamp(contentRows, 1, contentRows);
        }

        int cellWidth = Math.Max(1, Math.Min(width, measure.Max));
        if (imagePixelWidth.HasValue && imagePixelWidth.Value > 0 && imagePixelHeight.HasValue && imagePixelHeight.Value > 0) {
            double imageAspect = (double)imagePixelHeight.Value / imagePixelWidth.Value;
            double cellAspectRatio = GetTerminalCellAspectRatio();
            int estimatedRows = (int)Math.Ceiling(imageAspect * cellWidth * cellAspectRatio);
            return Math.Clamp(Math.Max(1, estimatedRows), 1, contentRows);
        }

        // Last fallback: keep as atomic item, but estimate from measured width.
        return Math.Clamp(Math.Max(1, (int)Math.Ceiling((double)measure.Max / width)), 1, contentRows);
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

        int totalLines = highlightedText.LineCount;
        int lastLineNumber = highlightedText.LineNumberStart + Math.Max(0, totalLines - 1);
        _stableLineNumberWidth = highlightedText.LineNumberWidth ?? lastLineNumber.ToString(CultureInfo.InvariantCulture).Length;
        _originalLineNumberStart = highlightedText.LineNumberStart;
        _originalLineNumberWidth = highlightedText.LineNumberWidth;

        // Reference the underlying renderable array directly to avoid copying.
        _renderables = highlightedText.Renderables;
        _top = 0;
    }

    public Pager(IEnumerable<IRenderable> renderables) {
        var list = renderables?.ToList();
        _renderables = list is null ? [] : (IReadOnlyList<IRenderable>)list;
        _top = 0;
    }
    private void Navigate(LiveDisplayContext ctx) {
        bool running = true;
        (WindowWidth, WindowHeight) = GetPagerSize();
        bool forceRedraw = true;

        while (running) {
            (int width, int pageHeight) = GetPagerSize();
            // Reserve last row for footer
            int contentRows = Math.Max(1, pageHeight - 1);

            bool resized = width != WindowWidth || pageHeight != WindowHeight;
            if (resized) {
                AnsiConsole.Console.Profile.Width = width;

                WindowWidth = width;
                WindowHeight = pageHeight;
                VTHelpers.ReserveRow(Math.Max(1, pageHeight - 1));
                forceRedraw = true;
            }

            // Redraw if needed (initial, resize, or after navigation)
            if (resized || forceRedraw) {
                RecalculateRenderableHeights(width, contentRows);
                _top = Math.Clamp(_top, 0, GetMaxTop(contentRows));
                ViewportWindow viewport = BuildViewport(_top, contentRows);
                _top = viewport.Top;

                bool fullClear = resized || viewport.HasImages || _lastPageHadImages;
                if (fullClear) {
                    VTHelpers.ClearScreen();
                    VTHelpers.ReserveRow(contentRows);
                }
                else {
                    VTHelpers.SetCursorPosition(1, 1);
                }

                IRenderable target = BuildRenderable(viewport);
                ctx.UpdateTarget(target);
                ctx.Refresh();

                DrawFooter(width, contentRows, viewport);

                // Clear any previously-rendered lines that are now beyond contentRows.
                if (_lastRenderedRows > contentRows) {
                    for (int r = contentRows + 1; r <= _lastRenderedRows; r++) {
                        VTHelpers.ClearRow(r);
                    }
                }

                _lastRenderedRows = contentRows;
                _lastPageHadImages = viewport.HasImages;
                forceRedraw = false;
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

    private IRenderable BuildRenderable(ViewportWindow viewport) {
        if (viewport.Count <= 0) {
            return new Rows([]);
        }

        if (_sourceHighlightedText is not null) {
            _sourceHighlightedText.SetView(_renderables, viewport.Top, viewport.Count);
            _sourceHighlightedText.LineNumberStart = (_originalLineNumberStart ?? 1) + viewport.Top;
            _sourceHighlightedText.LineNumberWidth = _stableLineNumberWidth;

            return _sourceHighlightedText;
        }

        return new Rows(_renderables.Skip(viewport.Top).Take(viewport.Count));
    }

    private void DrawFooter(int width, int contentRows, ViewportWindow viewport) {
        int total = _renderables.Count;
        int pos = total == 0 ? 0 : viewport.Top + 1;
        int end = viewport.EndExclusive;

        string keys = "Up/Down: ↑↓  PgUp/PgDn: PgUp/PgDn/Spacebar  Home/End: Home/End  q/Esc: Quit";
        string status = $" {pos}-{end}/{total} ";
        int remaining = Math.Max(0, width - keys.Length - status.Length - 2);
        string spacer = new(' ', remaining);
        string line = keys + spacer + status;
        if (line.Length > width) line = line[..width];

        // Write footer directly to reserved row (contentRows + 1)
        int footerRow = contentRows + 1;
        VTHelpers.SetCursorPosition(footerRow, 1);
        Console.Write(line.PadRight(width));
    }

    private void NavigateDirect(bool useAlternateBuffer) {
        bool running = true;
        (WindowWidth, WindowHeight) = GetPagerSize();
        bool forceRedraw = true;

        while (running) {
            (int width, int pageHeight) = GetPagerSize();
            int contentRows = Math.Max(1, pageHeight - 1);

            bool resized = width != WindowWidth || pageHeight != WindowHeight;
            if (resized) {
                AnsiConsole.Console.Profile.Width = width;
                WindowWidth = width;
                WindowHeight = pageHeight;
                forceRedraw = true;
            }

            if (resized || forceRedraw) {
                RecalculateRenderableHeights(width, contentRows);
                _top = Math.Clamp(_top, 0, GetMaxTop(contentRows));
                ViewportWindow viewport = BuildViewport(_top, contentRows);
                _top = viewport.Top;

                VTHelpers.ClearScreen();
                if (useAlternateBuffer) {
                    VTHelpers.ReserveRow(contentRows);
                }

                IRenderable target = BuildRenderable(viewport);
                AnsiConsole.Write(target);
                DrawFooter(width, contentRows, viewport);
                forceRedraw = false;
            }

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

    public void Show() => Show(useAlternateBuffer: true);

    public void Show(bool useAlternateBuffer) {
        if (useAlternateBuffer) {
            VTHelpers.EnterAlternateBuffer();
        }
        VTHelpers.HideCursor();
        try {
            // Sixel/pixel renderables are safest when written directly because
            // Live's diff/crop pass can interfere with terminal image sequences.
            if (ContainsImageRenderables()) {
                NavigateDirect(useAlternateBuffer);
                return;
            }

            (int width, int pageHeight) = GetPagerSize();
            int contentRows = Math.Max(1, pageHeight - 1);

            // Start with a clean screen then reserve the last row as a non-scrolling footer region
            if (useAlternateBuffer) {
                VTHelpers.ReserveRow(Math.Max(1, pageHeight - 1));
            }

            // Initial target for Spectre Live (footer is drawn manually)
            AnsiConsole.Console.Profile.Width = width;
            RecalculateRenderableHeights(width, contentRows);
            ViewportWindow initialViewport = BuildViewport(_top, contentRows);
            _top = initialViewport.Top;
            IRenderable initial = BuildRenderable(initialViewport);
            _lastRenderedRows = contentRows;
            _lastPageHadImages = initialViewport.HasImages;

            // If the initial page contains images, clear appropriately to ensure safe image rendering
            if (initialViewport.HasImages) {
                if (useAlternateBuffer) {
                    VTHelpers.ClearScreen();
                    VTHelpers.ReserveRow(Math.Max(1, pageHeight - 1));
                }
                else {
                    VTHelpers.ClearScreen();
                }
            }

            AnsiConsole.Live(initial)
            .AutoClear(true)
            .Overflow(VerticalOverflow.Crop)
            .Cropping(VerticalOverflowCropping.Bottom)
            .Start(ctx => {
                // Draw footer once before entering the interactive loop
                DrawFooter(width, contentRows, initialViewport);
                // Enter interactive loop using the live display context
                Navigate(ctx);
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
            }
            // Reset scroll region and restore normal screen buffer if used
            if (useAlternateBuffer) {
                VTHelpers.ResetScrollRegion();
                VTHelpers.ExitAlternateBuffer();
            }
            VTHelpers.ShowCursor();
        }
    }

    public void Dispose() {
        // No resources to dispose, but required for IDisposable
    }
}
