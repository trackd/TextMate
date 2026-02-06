using Spectre.Console;
using Spectre.Console.Rendering;

namespace PSTextMate.Core;

/// <summary>
/// Represents syntax-highlighted text ready for rendering.
/// Provides a clean, consistent output type.
/// Implements IRenderable so it can be used directly with Spectre.Console.
/// </summary>
public sealed class HighlightedText : Renderable {
    /// <summary>
    /// The highlighted renderables ready for display.
    /// </summary>
    public required IRenderable[] Renderables { get; init; }

    /// <summary>
    /// Number of lines contained in this highlighted text.
    /// </summary>
    public int LineCount => Renderables.Length;

    /// <summary>
    /// Renders the highlighted text by combining all renderables into a single output.
    /// </summary>
    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
        // Delegate to Rows which efficiently renders all renderables
        var rows = new Rows(Renderables);
        return ((IRenderable)rows).Render(options, maxWidth);
    }

    /// <summary>
    /// Measures the dimensions of the highlighted text.
    /// </summary>
    protected override Measurement Measure(RenderOptions options, int maxWidth) {
        // Delegate to Rows for measurement
        var rows = new Rows(Renderables);
        return ((IRenderable)rows).Measure(options, maxWidth);
    }

    /// <summary>
    /// Wraps the highlighted text in a Spectre.Console Panel.
    /// </summary>
    /// <param name="title">Optional panel title</param>
    /// <param name="border">Border style to use (default: Rounded)</param>
    /// <returns>Panel containing the highlighted text</returns>
    public Panel ToPanel(string? title = null, BoxBorder? border = null) {
        Panel panel = new(new Rows([.. Renderables]));

        if (!string.IsNullOrEmpty(title)) {
            panel.Header(title);
        }

        if (border != null) {
            panel.Border(border);
        }
        else {
            panel.Border(BoxBorder.Rounded);
        }

        return panel;
    }

    // public override string ToString() => ToPanel();

    /// <summary>
    /// Wraps the highlighted text with padding.
    /// </summary>
    /// <param name="padding">Padding to apply</param>
    /// <returns>Padder containing the highlighted text</returns>
    public Padder WithPadding(Padding padding) => new(new Rows([.. Renderables]), padding);

    /// <summary>
    /// Wraps the highlighted text with uniform padding on all sides.
    /// </summary>
    /// <param name="size">Padding size for all sides</param>
    /// <returns>Padder containing the highlighted text</returns>
    public Padder WithPadding(int size) => new(new Rows([.. Renderables]), new Padding(size));
}
