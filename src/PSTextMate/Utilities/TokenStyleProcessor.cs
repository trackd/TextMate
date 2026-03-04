using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PSTextMate.Core;

/// <summary>
/// Processes tokens and applies TextMate styling to produce Spectre renderables.
/// Decoupled from specific rendering context (can be used in code blocks, inline code, etc).
/// </summary>
internal static class TokenStyleProcessor {
    /// <summary>
    /// Processes tokens from a single line and produces styled Text objects.
    /// </summary>
    /// <param name="tokens">Tokens from grammar tokenization</param>
    /// <param name="line">Source line text</param>
    /// <param name="theme">Theme for color lookup</param>
    /// <param name="styler">Styler instance (inject for testability)</param>
    /// <returns>Array of styled Text renderables</returns>
    public static IRenderable[] ProcessTokens(
        IToken[] tokens,
        string line,
        Theme theme,
        ITextMateStyler styler) {
        var result = new List<IRenderable>();

        foreach (IToken token in tokens) {
            int startIndex = Math.Min(token.StartIndex, line.Length);
            int endIndex = Math.Min(token.EndIndex, line.Length);

            // Skip empty tokens
            if (startIndex >= endIndex)
                continue;

            // Extract text
            string tokenText = line[startIndex..endIndex];

            // Get style for this token's scopes
            Style? style = styler.GetStyleForScopes(token.Scopes, theme);

            // Apply style and add to result
            result.Add(styler.ApplyStyle(tokenText, style));
        }

        return [.. result];
    }

    /// <summary>
    /// Process multiple lines of tokens and return combined renderables.
    /// </summary>
    public static IRenderable[] ProcessLines(
        (IToken[] tokens, string line)[] tokenizedLines,
        Theme theme,
        ITextMateStyler styler) {
        var result = new List<IRenderable>();

        foreach ((IToken[] tokens, string line) in tokenizedLines) {
            // Process each line
            IRenderable[] lineRenderables = ProcessTokens(tokens, line, theme, styler);

            // Wrap line's tokens in a Row
            if (lineRenderables.Length > 0)
                result.Add(new Rows(lineRenderables));
            else
                result.Add(Text.Empty);
        }

        return [.. result];
    }
}
