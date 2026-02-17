using PSTextMate.Core;
using Spectre.Console;
using TextMateSharp.Themes;

namespace PSTextMate.Utilities;

/// <summary>
/// Extension methods for converting TextMate themes and colors to Spectre.Console styling.
/// </summary>
public static class ThemeExtensions {
    /// <summary>
    /// Converts a TextMate theme to a Spectre.Console style.
    /// This is a placeholder - actual theming should be done via scope-based lookups.
    /// </summary>
    /// <param name="theme">The TextMate theme to convert.</param>
    /// <returns>A Spectre.Console style representing the TextMate theme.</returns>
    public static Style ToSpectreStyle(this Theme theme) => new(foreground: Color.Default, background: Color.Default);
    /// <summary>
    /// Converts a TextMate color to a Spectre.Console color.
    /// </summary>
    /// <param name="color">The TextMate color to convert.</param>
    /// <returns>A Spectre.Console color representing the TextMate color.</returns>
    // Try to use a more general color type, e.g. System.Drawing.Color or a custom struct/class
    // If theme.Foreground and theme.Background are strings (hex), parse them accordingly
    public static Color ToSpectreColor(this object color) {
        if (color is string hex && !string.IsNullOrWhiteSpace(hex)) {
            try {
                return StyleHelper.HexToColor(hex);
            }
            catch {
                return Color.Default;
            }
        }
        return Color.Default;
    }
    /// <summary>
    /// Converts a TextMate font style to a Spectre.Console font style.
    /// </summary>
    /// <param name="fontStyle">The TextMate font style to convert.</param>
    /// <returns>A Spectre.Console font style representing the TextMate font style.</returns>

    public static FontStyle ToSpectreFontStyle(this FontStyle fontStyle) {
        FontStyle result = FontStyle.None;
        if ((fontStyle & FontStyle.Italic) != 0)
            result |= FontStyle.Italic;
        if ((fontStyle & FontStyle.Bold) != 0)
            result |= FontStyle.Bold;
        if ((fontStyle & FontStyle.Underline) != 0)
            result |= FontStyle.Underline;
        return result;
    }
}
