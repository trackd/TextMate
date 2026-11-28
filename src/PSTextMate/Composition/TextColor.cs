namespace PSTextMate.Core;

/// <summary>
/// An immutable RGB color, a terminal palette entry, or the inherited terminal color. The default value inherits.
/// Palette entries are written as palette references so terminals keep the user's own colors for them.
/// </summary>
public readonly record struct TextColor {
    private readonly int _encoded;

    /// <summary>The RGB value; palette entries report the standard xterm value. -1 for the default color.</summary>
    public int Rgb => ColorValue.ToRgb(Value);
    public bool IsDefault => _encoded == 0;
    public bool IsPalette => ColorValue.IsPalette(Value);

    /// <summary>The terminal palette index (0-255), or -1 for an RGB or default color.</summary>
    public int PaletteIndex => IsPalette ? Value & 0xff : -1;

    /// <summary>The value stored in <see cref="TextStyle"/>: -1, an RGB value, or a palette reference.</summary>
    internal int Value => _encoded - 1;

    private TextColor(int value) {
        _encoded = value + 1;
    }

    internal static TextColor FromValue(int value) => value < 0 ? default : new(value);

    public static TextColor Default => default;
    public static TextColor Black => new(0);
    public static TextColor White => new(0xffffff);
    public static TextColor Gray => new(0x808080);
    public static TextColor Red => new(0xff0000);
    public static TextColor Green => new(0x008000);
    public static TextColor Lime => new(0x00ff00);
    public static TextColor Blue => new(0x0000ff);
    public static TextColor Yellow => new(0xffff00);
    public static TextColor Cyan => new(0x00ffff);
    public static TextColor Magenta => new(0xff00ff);

    public static TextColor FromRgb(byte red, byte green, byte blue) => new((red << 16) | (green << 8) | blue);
    public static TextColor FromRgb(int rgb) {
        ArgumentOutOfRangeException.ThrowIfNegative(rgb);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rgb, 0xffffff);
        return new(rgb);
    }

    /// <summary>A terminal palette entry: 0-15 are the themeable ANSI colors, 16-255 the extended palette.</summary>
    public static TextColor FromPalette(int index) {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 255);
        return new(ColorValue.Palette(index));
    }

    /// <summary>Accepts #RGB, #RRGGBB, palette:N, or default. Parsing is independent of culture.</summary>
    public static TextColor Parse(string value) {
        ArgumentNullException.ThrowIfNull(value);
        return Parse(value.AsSpan());
    }
    public static TextColor Parse(ReadOnlySpan<char> value) => TryParse(value, out TextColor color)
        ? color : throw new FormatException("Expected #RGB, #RRGGBB, palette:N, or default.");

    public static bool TryParse(ReadOnlySpan<char> value, out TextColor color) {
        color = default;
        if (value.Equals("default", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.StartsWith("palette:", StringComparison.OrdinalIgnoreCase)) {
            if (!int.TryParse(value["palette:".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index > 255)
                return false;
            color = FromPalette(index);
            return true;
        }
        if (value.IsEmpty || value[0] != '#')
            return false;
        value = value[1..];
        if (value.Length is not (3 or 6) || !int.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int rgb))
            return false;
        if (value.Length == 3)
            rgb = (((rgb >> 8) & 15) * 0x110000) | (((rgb >> 4) & 15) * 0x1100) | ((rgb & 15) * 0x11);
        color = new(rgb);
        return true;
    }

    public override string ToString() => IsDefault ? "default"
        : IsPalette ? string.Create(CultureInfo.InvariantCulture, $"palette:{PaletteIndex}")
        : $"#{Rgb:X6}";
}

/// <summary>
/// The int color stored in <see cref="TextStyle"/>: -1 inherits, 0x000000-0xFFFFFF is RGB, and
/// <see cref="PaletteFlag"/> | index references a terminal palette entry.
/// </summary>
internal static class ColorValue {
    internal const int PaletteFlag = 0x1000000;

    internal static bool IsPalette(int value) => value >= PaletteFlag;
    internal static int Palette(int index) => PaletteFlag | index;
    internal static int ToRgb(int value) => value < 0 ? -1 : IsPalette(value) ? PaletteRgb(value & 0xff) : value;

    private static ReadOnlySpan<int> Basic => [0x000000, 0x800000, 0x008000, 0x808000, 0x000080, 0x800080, 0x008080, 0xc0c0c0,
        0x808080, 0xff0000, 0x00ff00, 0xffff00, 0x0000ff, 0xff00ff, 0x00ffff, 0xffffff];

    /// <summary>The standard xterm value of a palette entry; terminals may theme entries 0-15.</summary>
    internal static int PaletteRgb(int index) {
        if (index < 16)
            return Basic[index];
        if (index >= 232)
            return (8 + ((index - 232) * 10)) * 0x010101;
        index -= 16;
        return (Level(index / 36) << 16) | (Level(index / 6 % 6) << 8) | Level(index % 6);
    }

    private static int Level(int value) => value == 0 ? 0 : 55 + (value * 40);

    /// <summary>The nearest entry of the 6x6x6 cube or gray ramp (16-255).</summary>
    internal static int Nearest256(int rgb) {
        int r = (rgb >> 16) & 255, g = (rgb >> 8) & 255, b = rgb & 255;
        int cube = 16 + (36 * CubeStep(r)) + (6 * CubeStep(g)) + CubeStep(b);
        int average = (r + g + b) / 3;
        int gray = average < 4 ? 16 : average > 243 ? 231 : 232 + Math.Clamp((average - 3) / 10, 0, 23);
        return Distance(rgb, PaletteRgb(gray)) < Distance(rgb, PaletteRgb(cube)) ? gray : cube;
    }

    /// <summary>The nearest of the sixteen ANSI colors.</summary>
    internal static int Nearest16(int rgb) {
        int best = 0;
        int bestDistance = int.MaxValue;
        for (int index = 0; index < 16; index++) {
            int distance = Distance(rgb, Basic[index]);
            if (distance < bestDistance) {
                best = index;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static int CubeStep(int component) => component < 48 ? 0 : component < 115 ? 1 : (component - 35) / 40;

    private static int Distance(int left, int right) {
        int r = ((left >> 16) & 255) - ((right >> 16) & 255);
        int g = ((left >> 8) & 255) - ((right >> 8) & 255);
        int b = (left & 255) - (right & 255);
        // Weighted for perceived brightness.
        return (2 * r * r) + (4 * g * g) + (3 * b * b);
    }
}
