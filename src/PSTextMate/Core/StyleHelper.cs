using Spectre.Console;
using TextMateSharp.Themes;

namespace PSTextMate.Core;

/// <summary>
/// Provides utility methods for style and color conversion operations.
/// Handles conversion between TextMate and Spectre Console styling systems.
/// </summary>
internal static class StyleHelper {
    /// <summary>
    /// Converts a theme color ID to a Spectre Console Color.
    /// </summary>
    /// <param name="colorId">Color ID from theme</param>
    /// <param name="theme">Theme containing color definitions</param>
    /// <returns>Spectre Console Color instance</returns>
    public static Color GetColor(int colorId, Theme theme)
        => colorId == -1 ? Color.Default : HexToColor(theme.GetColor(colorId));

    /// <summary>
    /// Converts TextMate font style to Spectre Console decoration.
    /// </summary>
    /// <param name="fontStyle">TextMate font style</param>
    /// <returns>Spectre Console decoration</returns>
    public static Decoration GetDecoration(FontStyle fontStyle) {
        Decoration result = Decoration.None;
        if (fontStyle == FontStyle.NotSet)
            return result;
        if ((fontStyle & FontStyle.Italic) != 0)
            result |= Decoration.Italic;
        if ((fontStyle & FontStyle.Underline) != 0)
            result |= Decoration.Underline;
        if ((fontStyle & FontStyle.Bold) != 0)
            result |= Decoration.Bold;
        return result;
    }

    /// <summary>
    /// Converts a hex color string to a Spectre Console Color.
    /// </summary>
    /// <param name="hexString">Hex color string (with or without #)</param>
    /// <returns>Spectre Console Color instance</returns>
    public static Color HexToColor(string hexString) {
        if (hexString.StartsWith('#')) {
            hexString = hexString[1..];
        }

        byte[] c = Convert.FromHexString(hexString);
        return new Color(c[0], c[1], c[2]);
    }
}
