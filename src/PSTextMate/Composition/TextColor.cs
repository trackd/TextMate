namespace PSTextMate.Core;

/// <summary>An immutable RGB color, or the inherited terminal color. The default value inherits.</summary>
public readonly record struct TextColor {
    private readonly int _encoded;
    public int Rgb => _encoded - 1;
    public bool IsDefault => _encoded == 0;
    private TextColor(int rgb) {
        _encoded = rgb + 1;
    }

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

    /// <summary>Accepts #RGB, #RRGGBB, or default. Parsing is independent of culture.</summary>
    public static TextColor Parse(string value) {
        ArgumentNullException.ThrowIfNull(value);
        return Parse(value.AsSpan());
    }
    public static TextColor Parse(ReadOnlySpan<char> value) => TryParse(value, out TextColor color)
        ? color : throw new FormatException("Expected #RGB, #RRGGBB, or default.");

    public static bool TryParse(ReadOnlySpan<char> value, out TextColor color) {
        color = default;
        if (value.Equals("default", StringComparison.OrdinalIgnoreCase))
            return true;
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

    public override string ToString() => IsDefault ? "default" : $"#{Rgb:X6}";
}
