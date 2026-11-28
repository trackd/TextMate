namespace PSTextMate.Core;

/// <summary>Terminal graphics capabilities, from <see cref="TerminalCapabilities"/>.</summary>
public readonly record struct TerminalGraphics(bool Sixel, int CellWidth, int CellHeight, bool Kitty = false, bool InlineImage = false) {
    internal bool CanDraw => (Sixel || Kitty || InlineImage) && HasCellSize;
    internal bool HasCellSize => CellWidth is > 0 and <= 128 && CellHeight is > 0 and <= 256;
}
