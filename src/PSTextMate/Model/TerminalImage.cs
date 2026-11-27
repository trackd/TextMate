namespace PSTextMate.Core;

/// <summary>A module-generated Sixel image with a portable cell footprint.</summary>
public sealed class TerminalImage {
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<char> Sixel { get; }
    internal TerminalImage(int width, int height, ReadOnlyMemory<char> sixel) {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        Sixel = sixel;
    }
}

/// <summary>UTF-16 offset of the top-left cell of an image's portable fallback.</summary>
public readonly record struct PlacedImage(int Start, TerminalImage Image);
