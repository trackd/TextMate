namespace PSTextMate.Core;

public static partial class VT {
    /// <summary>Writes a terminal palette color, preserving user customization of its first sixteen entries.</summary>
    public static void WriteIndexedColor(TextWriter writer, int index, bool background = false) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 255);
        Span<char> sequence = stackalloc char[12];
        Csi.AsSpan().CopyTo(sequence);
        int length = 2;
        if (index < 16) {
            int code = (background ? 40 : 30) + (index < 8 ? index : 60 + index - 8);
            code.TryFormat(sequence[length..], out int written, provider: CultureInfo.InvariantCulture);
            length += written;
        }
        else {
            (background ? "48;5;" : "38;5;").AsSpan().CopyTo(sequence[length..]);
            length += 5;
            index.TryFormat(sequence[length..], out int written, provider: CultureInfo.InvariantCulture);
            length += written;
        }
        sequence[length++] = 'm';
        writer.Write(sequence[..length]);
    }

    public static void WriteStyle(TextWriter writer, TextStyle style) {
        Span<char> sequence = stackalloc char[96];
        "\u001b[0".AsSpan().CopyTo(sequence);
        int length = 3;
        if (style.Decoration != TextDecoration.None) {
            ReadOnlySpan<TextDecoration> flags = [TextDecoration.Bold, TextDecoration.Dim, TextDecoration.Italic, TextDecoration.Underline,
                TextDecoration.Strikethrough, TextDecoration.SlowBlink, TextDecoration.RapidBlink, TextDecoration.Invert, TextDecoration.Conceal];
            ReadOnlySpan<char> codes = "123495678";
            for (int index = 0; index < flags.Length; index++) {
                if ((style.Decoration & flags[index]) == 0)
                    continue;
                sequence[length++] = ';';
                sequence[length++] = codes[index];
            }
        }
        WriteColor(sequence, ref length, style.Foreground, false);
        WriteColor(sequence, ref length, style.Background, true);
        sequence[length++] = 'm';
        writer.Write(sequence[..length]);
    }

    private static void WriteColor(Span<char> destination, ref int length, int rgb, bool background) {
        if (rgb < 0)
            return;
        (background ? ";48;2;" : ";38;2;").AsSpan().CopyTo(destination[length..]);
        length += 6;
        for (int shift = 16; shift >= 0; shift -= 8) {
            ((rgb >> shift) & 255).TryFormat(destination[length..], out int written, provider: CultureInfo.InvariantCulture);
            length += written;
            if (shift > 0)
                destination[length++] = ';';
        }
    }

}
