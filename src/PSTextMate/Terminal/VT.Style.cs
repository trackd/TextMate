namespace PSTextMate.Core;

/// <summary>The colors a terminal can display. RGB colors are reduced to the nearest supported color.</summary>
public enum TerminalColorDepth {
    /// <summary>The sixteen ANSI colors.</summary>
    Standard,
    /// <summary>The 256-color palette.</summary>
    EightBit,
    /// <summary>24-bit RGB.</summary>
    TrueColor
}

public static partial class VT {
    private static int _colorDepth = -1;

    /// <summary>
    /// The color depth used for RGB colors. Detected from the environment like Spectre.Console does
    /// (COLORTERM, TERM, the Windows version); set it to override detection.
    /// </summary>
    public static TerminalColorDepth ColorDepth {
        get {
            int depth = Volatile.Read(ref _colorDepth);
            if (depth < 0) {
                depth = (int)DetectColorDepth();
                Volatile.Write(ref _colorDepth, depth);
            }
            return (TerminalColorDepth)depth;
        }
        set {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref _colorDepth, (int)value);
        }
    }

    internal static TerminalColorDepth DetectColorDepth() => DetectColorDepth(Environment.GetEnvironmentVariable,
        OperatingSystem.IsWindows(), Environment.OSVersion.Version);

    internal static TerminalColorDepth DetectColorDepth(Func<string, string?> environment, bool windows, Version osVersion) {
        string colorTerm = environment("COLORTERM") ?? string.Empty;
        if (colorTerm.Equals("truecolor", StringComparison.OrdinalIgnoreCase) || colorTerm.Equals("24bit", StringComparison.OrdinalIgnoreCase))
            return TerminalColorDepth.TrueColor;
        if (windows)
            return osVersion >= new Version(10, 0, 15063) ? TerminalColorDepth.TrueColor : TerminalColorDepth.EightBit;
        if (!string.IsNullOrEmpty(environment("WT_SESSION")))
            return TerminalColorDepth.TrueColor;
        string program = environment("TERM_PROGRAM") ?? string.Empty;
        if (program is "iTerm.app" or "WezTerm" or "vscode" or "ghostty")
            return TerminalColorDepth.TrueColor;
        string term = environment("TERM") ?? string.Empty;
        if (term.EndsWith("-direct", StringComparison.Ordinal) || term.Contains("truecolor", StringComparison.Ordinal)
            || term is "xterm-kitty" or "xterm-ghostty" or "alacritty" or "contour" or "wezterm" or "foot") {
            return TerminalColorDepth.TrueColor;
        }
        return term.EndsWith("-256color", StringComparison.Ordinal) || program == "Apple_Terminal"
            ? TerminalColorDepth.EightBit : TerminalColorDepth.Standard;
    }

    /// <summary>Writes a terminal palette color, preserving user customization of its first sixteen entries.</summary>
    public static void WriteIndexedColor(TextWriter writer, int index, bool background = false) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 255);
        Span<char> sequence = stackalloc char[12];
        Csi.AsSpan().CopyTo(sequence);
        int length = 2;
        WriteIndexed(sequence, ref length, index, background);
        sequence[length++] = 'm';
        writer.Write(sequence[..length]);
    }

    public static void WriteStyle(TextWriter writer, TextStyle style) => WriteStyle(writer, style, ColorDepth);

    public static void WriteStyle(TextWriter writer, TextStyle style, TerminalColorDepth depth) {
        ArgumentNullException.ThrowIfNull(writer);
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
        WriteColor(sequence, ref length, style.Foreground, false, depth);
        WriteColor(sequence, ref length, style.Background, true, depth);
        sequence[length++] = 'm';
        writer.Write(sequence[..length]);
    }

    private static void WriteColor(Span<char> destination, ref int length, int value, bool background, TerminalColorDepth depth) {
        if (value < 0)
            return;
        bool palette = ColorValue.IsPalette(value);
        if (palette || depth != TerminalColorDepth.TrueColor) {
            int index = palette ? value & 0xff
                : depth == TerminalColorDepth.EightBit ? ColorValue.Nearest256(value) : ColorValue.Nearest16(value);
            if (index >= 16 && depth == TerminalColorDepth.Standard)
                index = ColorValue.Nearest16(ColorValue.PaletteRgb(index));
            destination[length++] = ';';
            WriteIndexed(destination, ref length, index, background);
            return;
        }
        (background ? ";48;2;" : ";38;2;").AsSpan().CopyTo(destination[length..]);
        length += 6;
        for (int shift = 16; shift >= 0; shift -= 8) {
            ((value >> shift) & 255).TryFormat(destination[length..], out int written, provider: CultureInfo.InvariantCulture);
            length += written;
            if (shift > 0)
                destination[length++] = ';';
        }
    }

    private static void WriteIndexed(Span<char> destination, ref int length, int index, bool background) {
        if (index < 16) {
            int code = (background ? 40 : 30) + (index < 8 ? index : 60 + index - 8);
            code.TryFormat(destination[length..], out int written, provider: CultureInfo.InvariantCulture);
            length += written;
            return;
        }
        (background ? "48;5;" : "38;5;").AsSpan().CopyTo(destination[length..]);
        length += 5;
        index.TryFormat(destination[length..], out int count, provider: CultureInfo.InvariantCulture);
        length += count;
    }
}
