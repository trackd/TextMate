namespace PSTextMate.Terminal;

internal readonly record struct PagerViewportWindow(int Top, int Count, int EndExclusive, bool HasImages);

internal sealed class PagerViewportEngine {
    private readonly IReadOnlyList<IRenderable> _renderables;
    private readonly HighlightedText? _sourceHighlightedText;
    private List<int> _renderableHeights = [];

    public PagerViewportEngine(IReadOnlyList<IRenderable> renderables, HighlightedText? sourceHighlightedText) {
        _renderables = renderables ?? throw new ArgumentNullException(nameof(renderables));
        _sourceHighlightedText = sourceHighlightedText;
    }

    public void RecalculateHeights(int width, int contentRows, int windowHeight, IAnsiConsole console) {
        ArgumentNullException.ThrowIfNull(console);

        _renderableHeights = new List<int>(_renderables.Count);
        Capabilities capabilities = console.Profile.Capabilities;
        int measurementHeight = windowHeight > 0 ? windowHeight : Math.Max(1, contentRows + 3);
        var size = new Size(width, measurementHeight);
        var options = new RenderOptions(capabilities, size);

        for (int i = 0; i < _renderables.Count; i++) {
            IRenderable? renderable = _renderables[i];
            if (renderable is null) {
                _renderableHeights.Add(1);
                continue;
            }

            if (IsImageRenderable(renderable)) {
                if (renderable is PixelImage pixelImage) {
                    // In pager mode, clamp image width to the viewport so frames stay within screen bounds.
                    pixelImage.MaxWidth = pixelImage.MaxWidth is int existingWidth && existingWidth > 0
                        ? Math.Min(existingWidth, width)
                        : width;
                }

                _renderableHeights.Add(EstimateImageHeight(renderable, width, contentRows, options));
                continue;
            }

            try {
                // For non-image renderables, render to segments to get accurate row count.
                // This avoids overflow/cropping artifacts when wrapped text spans many rows.
                var segments = renderable.Render(options, width).ToList();
                int lines = CountLinesSegments(segments);
                _renderableHeights.Add(Math.Max(1, lines));
            }
            catch {
                // Fallback: assume single-line if measurement fails.
                _renderableHeights.Add(1);
            }
        }
    }

    public PagerViewportWindow BuildViewport(int proposedTop, int contentRows) {
        if (_renderables.Count == 0) {
            return new PagerViewportWindow(0, 0, 0, false);
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

        return new PagerViewportWindow(clampedTop, count, clampedTop + count, hasImages);
    }

    public int GetMaxTop(int contentRows) {
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

    public int ScrollTop(int currentTop, int delta, int contentRows) {
        if (_renderables.Count == 0) {
            return currentTop;
        }

        int direction = Math.Sign(delta);
        if (direction == 0) {
            return currentTop;
        }

        int maxTop = GetMaxTop(Math.Max(1, contentRows));
        return Math.Clamp(currentTop + direction, 0, maxTop);
    }

    public int PageDownTop(int currentTop, int contentRows) {
        if (_renderables.Count == 0) {
            return currentTop;
        }

        PagerViewportWindow viewport = BuildViewport(currentTop, contentRows);
        int maxTop = GetMaxTop(contentRows);
        return viewport.EndExclusive >= _renderables.Count ? maxTop : Math.Min(viewport.EndExclusive, maxTop);
    }

    public int PageUpTop(int currentTop, int contentRows) {
        if (_renderables.Count == 0) {
            return currentTop;
        }

        int rowsSkipped = 0;
        int idx = currentTop - 1;
        int nextTop = currentTop;
        while (idx >= 0 && rowsSkipped < contentRows) {
            rowsSkipped += Math.Clamp(GetRenderableHeight(idx), 1, contentRows);
            nextTop = idx;
            idx--;
        }

        return Math.Clamp(nextTop, 0, _renderables.Count - 1);
    }

    private int GetRenderableHeight(int index)
        => index < 0 || index >= _renderableHeights.Count ? 1 : Math.Max(1, _renderableHeights[index]);

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

    private bool IsMarkdownSource()
        => _sourceHighlightedText is not null
            && _sourceHighlightedText.Language.Contains("markdown", StringComparison.OrdinalIgnoreCase);

    private static int CountLinesSegments(List<Segment> segments) {
        if (segments.Count == 0) {
            return 0;
        }

        int lineBreaks = segments.Count(segment => segment.IsLineBreak);
        return lineBreaks == 0 ? 1 : segments[^1].IsLineBreak ? lineBreaks : lineBreaks + 1;
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

    private static double GetTerminalCellAspectRatio() {
        CellSize cellSize = Compatibility.GetCellSize();
        return cellSize.PixelWidth <= 0 || cellSize.PixelHeight <= 0
            ? 0.5d
            : (double)cellSize.PixelWidth / cellSize.PixelHeight;
    }
}
