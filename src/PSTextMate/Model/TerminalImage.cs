namespace PSTextMate.Core;

/// <summary>The terminal graphics protocol of a retained image payload.</summary>
public enum TerminalImageProtocol {
    /// <summary>DEC Sixel: <c>DCS … q … ST</c>.</summary>
    Sixel,
    /// <summary>Kitty graphics protocol: <c>APC G … ST</c>, possibly in several chunks.</summary>
    Kitty,
    /// <summary>iTerm2 inline images: <c>OSC 1337;File=… ST</c>.</summary>
    InlineImage,
    /// <summary>Good Image Protocol one-shot render: <c>DCS ! g o=s … ST</c>.</summary>
    GoodImage
}

/// <summary>
/// A complete terminal image sequence with its cell footprint. TextMate never decodes or re-encodes pixels:
/// it places the payload, reserves its cells and redraws it when the view moves.
/// </summary>
public sealed class TerminalImage {
    public int Width { get; }
    public int Height { get; }
    public TerminalImageProtocol Protocol { get; }
    /// <summary>The escape sequence(s) that draw the image at the cursor.</summary>
    public ReadOnlyMemory<char> Payload { get; }

    internal TerminalImage(int width, int height, ReadOnlyMemory<char> payload, TerminalImageProtocol protocol = TerminalImageProtocol.Sixel) {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (!Enum.IsDefined(protocol))
            throw new ArgumentOutOfRangeException(nameof(protocol));
        Width = width;
        Height = height;
        Payload = payload;
        Protocol = protocol;
    }
}

/// <summary>UTF-16 offset of the top-left cell of an image's portable fallback.</summary>
public readonly record struct PlacedImage(int Start, TerminalImage Image);
