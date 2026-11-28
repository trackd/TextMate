using PSTextMate.Core;

namespace PSTextMate.Images;

internal static class ImageSizing {
    internal static (int Width, int Height) Fit(ImageDimensions source, TerminalGraphics cells, int columns, int rows) {
        ArgumentOutOfRangeException.ThrowIfLessThan(source.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(source.Height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        int cellWidth = cells.CellWidth > 0 ? cells.CellWidth : 10;
        int cellHeight = cells.CellHeight > 0 ? cells.CellHeight : 20;
        // A Sixel band occupies six pixel rows. Older module versions can round beyond the cell request.
        int heightLimit = cells.Sixel && cellHeight % 6 != 0 ? Math.Max(1, rows - 1) : rows;
        double scale = Math.Min(1, Math.Min((double)columns * cellWidth / source.Width, (double)heightLimit * cellHeight / source.Height));
        int width = Math.Clamp((int)Math.Ceiling(source.Width * scale / cellWidth), 1, columns);
        int height = Math.Clamp((int)Math.Ceiling(source.Height * scale / cellHeight), 1, heightLimit);
        return (width, height);
    }
}
