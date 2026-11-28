namespace PSTextMate.Core;

[Flags]
public enum TextDecoration : ushort {
    None = 0, Italic = 1, Bold = 2, Underline = 4, Strikethrough = 8,
    Dim = 16, SlowBlink = 32, Invert = 64, Conceal = 128, RapidBlink = 256
}

/// <summary>Colors are -1 for the terminal default, RGB, or a palette reference; use <see cref="TextColor"/> to build or read them.</summary>
public readonly record struct TextStyle(int Foreground, int Background, TextDecoration Decoration, string? Hyperlink = null) {
    public static TextStyle Plain => new(-1, -1, TextDecoration.None);
    public TextStyle(TextColor foreground, TextColor background = default, TextDecoration decoration = TextDecoration.None, string? hyperlink = null)
        : this(foreground.Value, background.Value, decoration, hyperlink) {

    }
    public TextColor ForegroundColor => TextColor.FromValue(Foreground);
    public TextColor BackgroundColor => TextColor.FromValue(Background);
    public TextStyle WithForeground(TextColor color) => this with { Foreground = color.Value };
    public TextStyle WithBackground(TextColor color) => this with { Background = color.Value };
    public TextStyle WithDecoration(TextDecoration decoration) => this with { Decoration = decoration };
    public TextStyle WithHyperlink(string? uri) {
        if (uri is not null) {
            foreach (char value in uri) {
                if (char.IsControl(value))
                    throw new ArgumentException("Links cannot contain control characters.", nameof(uri));
            }
        }
        return this with { Hyperlink = uri };
    }
}
