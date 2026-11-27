namespace PSTextMate.Core;

/// <summary>Terminal graphics capabilities supplied by the optional image module.</summary>
public readonly record struct TerminalGraphics(bool Sixel, int CellWidth, int CellHeight) {
    internal bool CanDraw => Sixel && CellWidth is > 0 and <= 128 && CellHeight is > 0 and <= 256;
}
