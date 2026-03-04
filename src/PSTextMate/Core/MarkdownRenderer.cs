using Spectre.Console.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PSTextMate.Core;

/// <summary>
/// Facade for markdown rendering that adapts between TextMateProcessor's interface
/// and the Markdig-based renderer in PSTextMate.Rendering.
/// </summary>
internal static class MarkdownRenderer {
    /// <summary>
    /// Renders markdown content with compatibility layer for TextMateProcessor.
    /// </summary>
    /// <param name="lines">Markdown lines to render</param>
    /// <param name="theme">Theme for syntax highlighting</param>
    /// <param name="grammar">Grammar (unused, maintained for interface compatibility)</param>
    /// <param name="themeName">Theme name enumeration</param>
    /// <returns>Rendered markdown as IRenderable array</returns>
    public static IRenderable[] Render(string[] lines, Theme theme, IGrammar grammar, ThemeName themeName) {
        string markdown = string.Join('\n', lines);
        return Rendering.MarkdownRenderer.Render(markdown, theme, themeName);
    }
}
