namespace PSTextMate.Core;

/// <summary>A width-aware view over an immutable document. Owns a single cached layout.</summary>
public sealed class TextMateView : ITextRenderable {
    private readonly HighlightedDocument _source;
    private readonly RendererOptions _options;
    private readonly Lock _gate = new();
    private HighlightedDocument? _layout;
    private int _width;
    private int _height;

    public TextMateView(HighlightedDocument document, RendererOptions options = default) {
        ArgumentNullException.ThrowIfNull(document);
        _source = document.LayoutSource ?? document;
        _options = document.LayoutSource is null ? options : document.LayoutOptions;
        if (options.Width > 0)
            _options = _options with { Width = options.Width, ConstrainWidth = options.ConstrainWidth ?? _options.ConstrainWidth };
    }

    public HighlightedDocument Layout(int width) => Layout(width, _options.Height);

    /// <summary>Supplies the image viewport height without truncating the text layout.</summary>
    public HighlightedDocument Layout(int width, int height) {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        bool constrained = _options.ConstrainWidth ?? (_options.Width > 0);
        int layoutWidth = constrained && _options.Width > 0 ? Math.Min(width, _options.Width) : width;
        lock (_gate) {
            if (_layout is null || _width != layoutWidth || _height != height) {
                _layout = DocumentPresentation.Prepare(_source, _options with { Width = layoutWidth, Height = height });
                _width = layoutWidth;
                _height = height;
            }
            return _layout;
        }
    }
}
