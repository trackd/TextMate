using Spectre.Console;
using Spectre.Console.Rendering;

namespace PSTextMate.Rendering;

/// <summary>
/// Handles rendering of images at the block level with support for captions and layouts.
/// Demonstrates how to embed SixelImage in various Spectre.Console containers.
/// </summary>
internal static class ImageBlockRenderer {
    /// <summary>
    /// Renders an image with optional caption using appropriate container.
    /// SixelImage can be embedded in Panel, Columns, Rows, Grid, Table cells, or rendered directly.
    /// </summary>
    /// <param name="altText">Alternative text / caption for the image</param>
    /// <param name="imageUrl">URL or path to the image</param>
    /// <param name="renderMode">How to render the image (direct, panel, columns, rows)</param>
    /// <returns>A renderable containing the image</returns>
    public static IRenderable? RenderImageBlock(
        string altText,
        string imageUrl,
        ImageRenderMode renderMode = ImageRenderMode.Direct) {

        // Get the base image renderable (either SixelImage or fallback)
        IRenderable? imageRenderable = ImageRenderer.RenderImage(altText, imageUrl);

        if (imageRenderable is null) return null;

        // Apply the rendering mode to wrap/position the image
        return renderMode switch {
            // Render directly (no wrapper) - good for standalone images
            ImageRenderMode.Direct
                => imageRenderable,

            // Wrap in Panel with title - good for captioned images
            ImageRenderMode.PanelWithCaption when !string.IsNullOrEmpty(altText)
                => new Panel(imageRenderable)
                    .Header(altText.EscapeMarkup())
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Grey),

            // Wrap in Panel without title
            ImageRenderMode.PanelWithCaption
                => new Panel(imageRenderable)
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Grey),

            // Wrap with padding
            ImageRenderMode.WithPadding
                => new Padder(imageRenderable, new Padding(1, 0)),
            ImageRenderMode.SideCaption
                => RenderImageWithSideCaption(altText, imageUrl, altText),
            ImageRenderMode.VerticalCaption
                => RenderImageWithVerticalCaption(altText, imageUrl, altText),
            ImageRenderMode.Grid
                => RenderImageInGrid(altText, imageUrl, topCaption: altText),
            ImageRenderMode.TableCell
                => RenderImageInTable(altText, imageUrl, altText),
            _ => imageRenderable
        };
    }

    /// <summary>
    /// Renders image with text caption in a two-column layout (image | text).
    /// Demonstrates using Columns to embed SixelImage with other content.
    /// </summary>
    public static IRenderable? RenderImageWithSideCaption(
        string altText,
        string imageUrl,
        string caption) {

        IRenderable? imageRenderable = ImageRenderer.RenderImage(altText, imageUrl);
        if (imageRenderable is null) {
            return null;
        }

        // Create a captioned text panel using Text to avoid markup parsing
        var captionText = new Text(caption ?? string.Empty, Style.Plain);
        Panel captionPanel = new Panel(captionText)
            .Border(BoxBorder.None)
            .Padding(0, 1);  // Padding on sides

        // Arrange image and caption side-by-side using Columns
        // This is how you embed SixelImage (or any IRenderable) horizontally
        return new Columns(imageRenderable, captionPanel);
    }

    /// <summary>
    /// Renders image with caption stacked vertically (image on top, caption below).
    /// Demonstrates using Rows to embed SixelImage with other content.
    /// </summary>
    public static IRenderable? RenderImageWithVerticalCaption(
        string altText,
        string imageUrl,
        string caption) {

        IRenderable? imageRenderable = ImageRenderer.RenderImage(altText, imageUrl);
        if (imageRenderable is null) {
            return null;
        }

        // Create caption text
        var captionText2 = new Text(caption ?? string.Empty, Style.Plain);

        // Arrange vertically using Rows
        return new Rows(
            imageRenderable,
            new Padder(captionText2, new Padding(0, 1))  // Padding above caption
        );
    }

    /// <summary>
    /// Renders image in a grid layout with optional surrounding content.
    /// Demonstrates using Grid to embed SixelImage with flexible positioning.
    /// </summary>
    public static IRenderable? RenderImageInGrid(
        string altText,
        string imageUrl,
        string? topCaption = null,
        string? bottomCaption = null) {

        IRenderable? imageRenderable = ImageRenderer.RenderImage(altText, imageUrl);
        if (imageRenderable is null) {
            return null;
        }

        Grid grid = new Grid()
            .AddColumn(new GridColumn { NoWrap = false })
            .AddRow(new Text(topCaption ?? string.Empty, Style.Plain));

        grid.AddRow(imageRenderable);

        if (!string.IsNullOrEmpty(bottomCaption)) {
            grid.AddRow(new Text(bottomCaption, Style.Plain));
        }

        return grid;
    }

    /// <summary>
    /// Renders image in a table cell with text.
    /// Demonstrates embedding SixelImage in Table cells.
    /// </summary>
    public static Table RenderImageInTable(
        string altText,
        string imageUrl,
        string caption) {

        IRenderable? imageRenderable = ImageRenderer.RenderImage(altText, imageUrl);

        Table table = new Table()
            .AddColumn("Image")
            .AddColumn("Caption");

        if (imageRenderable is not null) {
            table.AddRow(imageRenderable, new Text(caption ?? string.Empty, Style.Plain));
        }
        else {
            table.AddRow(
                new Text($"Image failed to load: {imageUrl}", new Style(Color.Grey)),
                new Text(caption ?? string.Empty, Style.Plain)
            );
        }

        return table;
    }
}

/// <summary>
/// Specifies how an image should be rendered at the block level.
/// </summary>
internal enum ImageRenderMode {
    /// <summary>Render image directly without any wrapper (most efficient)</summary>
    Direct,

    /// <summary>Wrap image in a Panel with caption as header (good for titled images)</summary>
    PanelWithCaption,

    /// <summary>Wrap image with padding</summary>
    WithPadding,

    /// <summary>Side-by-side layout with caption (requires additional caption text)</summary>
    SideCaption,

    /// <summary>Vertical stack with caption (requires additional caption text)</summary>
    VerticalCaption,

    /// <summary>Grid layout (most flexible, requires additional content)</summary>
    Grid,

    /// <summary>Table cell (requires additional caption)</summary>
    TableCell
}
