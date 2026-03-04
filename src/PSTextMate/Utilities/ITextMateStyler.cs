using Spectre.Console;
using TextMateSharp.Themes;

namespace PSTextMate.Core;

/// <summary>
/// Abstraction for applying TextMate token styles to text.
/// Enables reuse of TextMate highlighting in different contexts
/// (code blocks, inline code, etc).
/// </summary>
public interface ITextMateStyler {
    /// <summary>
    /// Gets the Spectre Style for a token's scope hierarchy.
    /// </summary>
    /// <param name="scopes">Token scope hierarchy</param>
    /// <param name="theme">Theme for color lookup</param>
    /// <returns>Spectre Style or null if no style found</returns>
    Style? GetStyleForScopes(IEnumerable<string> scopes, Theme theme);

    /// <summary>
    /// Applies a style to text.
    /// </summary>
    /// <param name="text">Text to style</param>
    /// <param name="style">Style to apply (can be null)</param>
    /// <returns>Rendered text with style applied</returns>
    Text ApplyStyle(string text, Style? style);
}
