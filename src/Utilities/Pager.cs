using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;
using PSTextMate.Core;

namespace PSTextMate.Utilities;

/// <summary>
/// Simple pager implemented with Spectre.Console Live display.
/// Interaction keys:
/// - Up/Down: move one line
/// - PageUp/PageDown: move by page
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
    // Cached measured heights (in rows) for each renderable at the current width
    private List<int> _renderableHeights = [];
    private bool IsMarkdownSource()
        => _sourceHighlightedText is not null
            && _sourceHighlightedText.Language.Contains("markdown", StringComparison.OrdinalIgnoreCase);

    private bool PageContainsImages(int clampedTop, int pageHeight) {
        if (_sourceHighlightedText is not null && !IsMarkdownSource()) {
            return false;
        }

        foreach (IRenderable? r in _renderables.Skip(clampedTop).Take(pageHeight)) {
            if (r is null) continue;
            string name = r.GetType().Name;
            if (name.Contains("Sixel", StringComparison.OrdinalIgnoreCase) || name.Contains("Pixel", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Compute the maximum valid _top index such that starting at that index there
    // are at least `contentRows` rows available to render (based on
    // `_renderableHeights`). Falls back to a simple count-based heuristic when
    // heights are not known.
    private int MaxTopForContentRows(int contentRows) {
        if (contentRows <= 0) return 0;
        if (_renderableHeights == null || _renderableHeights.Count == 0) {
            return Math.Max(0, _renderables.Count - contentRows);
        }

        int n = _renderableHeights.Count;
        int[] suffix = new int[n];
        suffix[n - 1] = _renderableHeights[n - 1];
        for (int i = n - 2; i >= 0; i--) suffix[i] = suffix[i + 1] + _renderableHeights[i];

        for (int i = n - 1; i >= 0; i--) {
            if (suffix[i] >= contentRows) return i;
        }

        return 0;
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

                // Detect shrink (large -> small). If terminal shrank, do a full clear+redraw
                bool shrank = pageHeight < WindowHeight;

                WindowWidth = width;
                WindowHeight = pageHeight;

                // Clamp current top to the new page size so content doesn't jump
                int maxTopAfterResize = MaxTopForContentRows(contentRows);
                _top = Math.Clamp(_top, 0, maxTopAfterResize);

                if (shrank) {
                    // Full clear then update reserved scroll region to match new height.
                    // ClearScreenAlt resets terminal state, so set the scroll region after clearing.
                    VTHelpers.ClearScreenAlt();
                    VTHelpers.ResetScrollRegion();
                    VTHelpers.ReserveRow(Math.Max(1, pageHeight - 1));

                    // Immediate redraw via Spectre Live (already safe after ClearScreenAlt)
                    ctx.UpdateTarget(BuildRenderable(width, contentRows));
                    ctx.Refresh();
                    DrawFooter(width, contentRows);
                    _lastRenderedRows = contentRows;
                    forceRedraw = false;
                    // skip the later redraw block
                    continue;
                }
                // On grow or same-size width change, update reserved row and mark for redraw normally
                VTHelpers.ReserveRow(Math.Max(1, pageHeight - 1));
                forceRedraw = true;
            }

            // Redraw if needed (initial, resize, or after navigation)
            if (resized || forceRedraw) {
                // Avoid a full clear here to reduce flicker; update in-place instead
                VTHelpers.SetCursorPosition(1, 1);

                // Determine if this page contains image renderables that may emit raw sequences.
                int maxTop = MaxTopForContentRows(contentRows);
                int clampedTop = Math.Clamp(_top, 0, maxTop);
                bool pageHasImages = PageContainsImages(clampedTop, contentRows);

                if (pageHasImages) {
                    // Full clear + reserve ensures the terminal is in a known state before image output
                    VTHelpers.ClearScreenAlt();
                    VTHelpers.ResetScrollRegion();
                    VTHelpers.ReserveRow(Math.Max(1, pageHeight - 1));
                }

                // Recalculate per-renderable heights for current width so we can
                // page by renderable boundaries (important for multi-row images).
                if (_sourceHighlightedText is not null) {
                    _renderableHeights = [.. _sourceHighlightedText.MeasureRenderables(width)];
                }
                else {
                    RecalculateRenderableHeights(width);
                }

                // Update Spectre Live target (Spectre handles rendering and wrapping)
                IRenderable target = BuildRenderable(width, contentRows);
                ctx.UpdateTarget(target);
                ctx.Refresh();

                // Draw footer manually on reserved row
                DrawFooter(width, contentRows);

                // Clear any previously-rendered lines that are now beyond contentRows.
                if (_lastRenderedRows > contentRows) {
                    for (int r = contentRows + 1; r <= _lastRenderedRows; r++) {
                        VTHelpers.ClearRow(r);
                    }
                }

                _lastRenderedRows = contentRows;
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
    private void Scroll(int delta, int pageHeight) {
        int maxTop = Math.Max(0, _renderables.Count - pageHeight);
        _top = Math.Clamp(_top + delta, 0, maxTop);
    }

    // Scroll by renderable units. Uses _renderableHeights to advance by entries
    // rather than single rows so multi-row images are not split.
    private void ScrollRenderable(int delta, int contentRows) {
        if (_renderables.Count == 0) return;

        int direction = Math.Sign(delta);
        if (direction == 0) return;

        int candidate = _top + direction;
        candidate = Math.Clamp(candidate, 0, _renderables.Count - 1);

        // Clamp candidate to the maximum valid top based on contentRows so we don't
        // end up with a starting index that cannot produce a full page when using
        // renderable heights.
        int maxTop = MaxTopForContentRows(contentRows);
        candidate = Math.Clamp(candidate, 0, Math.Max(0, maxTop));

        _top = candidate;
    }

    private void PageDown(int contentRows) {
        if (_renderables.Count == 0) return;

        // Advance _top forward until we've skipped at least contentRows rows
        int rowsSkipped = 0;
        int idx = _top;
        while (idx < _renderables.Count && rowsSkipped < contentRows) {
            rowsSkipped += GetRenderableHeight(idx);
            idx++;
        }
        _top = Math.Clamp(idx, 0, Math.Max(0, _renderables.Count - 1));
    }

    private void PageUp(int contentRows) {
        if (_renderables.Count == 0) return;

        int rowsSkipped = 0;
        int idx = _top - 1;
        while (idx >= 0 && rowsSkipped < contentRows) {
            rowsSkipped += GetRenderableHeight(idx);
            idx--;
        }
        _top = Math.Clamp(idx + 1, 0, Math.Max(0, _renderables.Count - 1));
    }

    private int GetRenderableHeight(int index)
        => index < 0 || index >= _renderableHeights.Count ? 1 : _renderableHeights[index];

    private void RecalculateRenderableHeights(int width) {
        _renderableHeights = new List<int>(_renderables.Count);
        Capabilities capabilities = AnsiConsole.Console.Profile.Capabilities;
        var size = new Size(width, Math.Max(1, Console.WindowHeight));
        var options = new RenderOptions(capabilities, size);

        // To avoid side-effects (e.g. sixel/pixel images) during off-screen
        // measurement, only render a limited window around the current view
        // and skip obvious image-like renderables. Everything else is given a
        // conservative default of 1 row when not measured.
        int count = _renderables.Count;
        int window = Math.Max(1, WindowHeight > 0 ? WindowHeight : Console.WindowHeight);
        int measureStart = Math.Max(0, _top - window);
        int measureEnd = Math.Min(count, _top + window * 2); // lookahead a couple pages

        for (int i = 0; i < count; i++) {
            IRenderable? r = _renderables[i];
            if (r is null) {
                _renderableHeights.Add(0);
                continue;
            }

            // Only attempt to fully measure renderables in the nearby window.
            if (i < measureStart || i >= measureEnd) {
                _renderableHeights.Add(1);
                continue;
            }

            string name = r.GetType().Name;
            if (name.Contains("Sixel", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Pixel", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Image", StringComparison.OrdinalIgnoreCase)) {
                // Skip measuring image-like renderables to avoid side-effects.
                _renderableHeights.Add(1);
                continue;
            }

            try {
                var segments = r.Render(options, width).ToList();
                int lines = CountLinesSegments(segments);
                if (lines <= 0) lines = 1;
                _renderableHeights.Add(lines);
            }
            catch {
                // Fallback: assume single-line if rendering for measurement fails
                _renderableHeights.Add(1);
            }
        }
    }

    private static int CountLinesSegments(List<Segment> segments) {
        if (segments.Count == 0) {
            return 0;
        }

        int lineBreaks = segments.Count(segment => segment.IsLineBreak);
        return lineBreaks == 0 ? 1 : segments[^1].IsLineBreak ? lineBreaks : lineBreaks + 1;
    }
    private void GoToTop() => _top = 0;

    private void GoToEnd(int pageHeight)
        => _top = Math.Max(0, _renderables.Count - pageHeight);

    // Accepts dynamic width and pageHeight; footer is drawn outside Live target
    private IRenderable BuildRenderable(int width, int pageHeight) {
        int maxTop = Math.Max(0, _renderables.Count - pageHeight);
        int clampedTop = Math.Clamp(_top, 0, maxTop);
        int end = Math.Min(clampedTop + pageHeight, _renderables.Count);
        var pageRenderables = _renderables.Skip(clampedTop).Take(end - clampedTop).ToList();

        if (_sourceHighlightedText is not null) {
            // Configure the provided HighlightedText instance to view the current
            // page of underlying renderables. Do not allocate a new HighlightedText
            // — mutate the view on the source instance and let its Render/Measure
            // handle panel wrapping and line-numbering.
            _sourceHighlightedText.SetView(_renderables, clampedTop, end - clampedTop);
            _sourceHighlightedText.LineNumberStart = (_originalLineNumberStart ?? 1) + clampedTop;
            _sourceHighlightedText.LineNumberWidth = _stableLineNumberWidth;

            return _sourceHighlightedText;
        }

        // Avoid allocating a new list/array for the page; use a deferred enumerable.
        return new Rows(_renderables.Skip(clampedTop).Take(end - clampedTop));
    }

    private void DrawFooter(int width, int contentRows) {
        int maxTop = Math.Max(0, _renderables.Count - contentRows);
        int clampedTop = Math.Clamp(_top, 0, maxTop);
        int end = Math.Min(clampedTop + contentRows, _renderables.Count);
        int total = _renderables.Count;
        int pos = Math.Min(clampedTop + 1, Math.Max(0, total));

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

    public void Show() {
        // Switch to alternate screen buffer
        VTHelpers.EnterAlternateBuffer();
        VTHelpers.HideCursor();
        // Console.CursorVisible = false;
        try {
            (int width, int pageHeight) = GetPagerSize();
            int contentRows = Math.Max(1, pageHeight - 1);

            // Start with a clean screen then reserve the last row as a non-scrolling footer region
            // VTHelpers.ClearScreen();
            VTHelpers.ReserveRow(Math.Max(1, pageHeight - 1));

            // Initial target for Spectre Live (footer is drawn manually)
            AnsiConsole.Console.Profile.Width = width;
            IRenderable initial = BuildRenderable(width, contentRows);
            _lastRenderedRows = contentRows;

            // If the initial page contains images, clear+reserve to ensure safe image rendering
            int initialMaxTop = Math.Max(0, _renderables.Count - contentRows);
            int initialClamped = Math.Clamp(_top, 0, initialMaxTop);
            if (PageContainsImages(initialClamped, contentRows)) {
                VTHelpers.ClearScreenAlt();
                VTHelpers.ResetScrollRegion();
                VTHelpers.ReserveRow(Math.Max(1, pageHeight - 1));
            }

            AnsiConsole.Live(initial).Start(ctx => {
                // Draw footer once before entering the interactive loop
                DrawFooter(width, contentRows);
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
            // Reset scroll region and restore normal screen buffer
            VTHelpers.ResetScrollRegion();
            VTHelpers.ShowCursor();
            VTHelpers.ExitAlternateBuffer();
            // Console.CursorVisible = true;
        }
    }

    public void Dispose() {
        // No resources to dispose, but required for IDisposable
    }
}
