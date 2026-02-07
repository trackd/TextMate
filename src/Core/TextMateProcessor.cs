using System.Text;
using PSTextMate.Core;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PSTextMate.Core;

/// <summary>
/// Main entry point for TextMate processing operations.
/// Provides high-performance text processing using TextMate grammars and themes.
/// </summary>
public static class TextMateProcessor {
    /// <summary>
    /// Processes string lines for code blocks without escaping markup characters.
    /// This preserves raw source code content for proper syntax highlighting.
    /// </summary>
    /// <param name="lines">Array of text lines to process</param>
    /// <param name="themeName">Theme to apply for styling</param>
    /// <param name="grammarId">Language ID or file extension for grammar selection</param>
    /// <param name="isExtension">True if grammarId is a file extension, false if it's a language ID</param>
    /// <returns>Rendered rows with syntax highlighting, or null if processing fails</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when grammar cannot be found or processing encounters an error</exception>
    public static IRenderable[]? ProcessLines(string[] lines, ThemeName themeName, string grammarId, bool isExtension, bool forceAlternate = false) {
        ArgumentNullException.ThrowIfNull(lines, nameof(lines));

        if (lines.Length == 0 || lines.AllIsNullOrEmpty()) {
            return null;
        }

        try {
            (TextMateSharp.Registry.Registry registry, Theme theme) = CacheManager.GetCachedTheme(themeName);
            // Resolve grammar using CacheManager which knows how to map language ids and extensions
            IGrammar? grammar = CacheManager.GetCachedGrammar(registry, grammarId, isExtension) ?? throw new InvalidOperationException(isExtension ? $"Grammar not found for file extension: {grammarId}" : $"Grammar not found for language: {grammarId}");

            // if alternate it will use TextMate for markdown as well.
            return grammar.GetName() == "Markdown" && forceAlternate
                ? StandardRenderer.Render(lines, theme, grammar)
                : (grammar.GetName() == "Markdown")
                ? MarkdownRenderer.Render(lines, theme, grammar, themeName)
                : StandardRenderer.Render(lines, theme, grammar);
        }
        catch (InvalidOperationException) {
            throw;
        }
        catch (ArgumentException ex) {
            throw new InvalidOperationException($"Argument error processing lines with grammar '{grammarId}': {ex.Message}", ex);
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Unexpected error processing lines with grammar '{grammarId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Processes string lines for code blocks without escaping markup characters.
    /// This preserves raw source code content for proper syntax highlighting.
    /// </summary>
    /// <param name="lines">Array of text lines to process</param>
    /// <param name="themeName">Theme to apply for styling</param>
    /// <param name="grammarId">Language ID or file extension for grammar selection</param>
    /// <param name="isExtension">True if grammarId is a file extension, false if it's a language ID</param>
    /// <returns>Rendered rows with syntax highlighting, or null if processing fails</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when grammar cannot be found or processing encounters an error</exception>
    public static IRenderable[]? ProcessLinesCodeBlock(string[] lines, ThemeName themeName, string grammarId, bool isExtension = false) {
        ArgumentNullException.ThrowIfNull(lines, nameof(lines));

        try {
            (TextMateSharp.Registry.Registry registry, Theme theme) = CacheManager.GetCachedTheme(themeName);
            IGrammar? grammar = CacheManager.GetCachedGrammar(registry, grammarId, isExtension);

            if (grammar is null) {
                string errorMessage = isExtension
                    ? $"Grammar not found for file extension: {grammarId}"
                    : $"Grammar not found for language: {grammarId}";
                throw new InvalidOperationException(errorMessage);
            }

            // Always use StandardRenderer for code blocks, never MarkdownRenderer
            return RenderCodeBlock(lines, theme, grammar);
        }
        catch (InvalidOperationException) {
            throw;
        }
        catch (ArgumentException ex) {
            throw new InvalidOperationException($"Argument error processing code block with grammar '{grammarId}': {ex.Message}", ex);
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Unexpected error processing code block with grammar '{grammarId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Renders code block lines without escaping markup characters.
    /// </summary>
    private static IRenderable[] RenderCodeBlock(string[] lines, Theme theme, IGrammar grammar) {
        List<IRenderable> rows = new(lines.Length);
        IStateStack? ruleStack = null;
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
            if (string.IsNullOrEmpty(lines[lineIndex])) {
                rows.Add(new Rows(Text.Empty));
                continue;
            }
            var paragraph = new Paragraph();
            ITokenizeLineResult result = grammar.TokenizeLine(lines[lineIndex], ruleStack, TimeSpan.MaxValue);
            ruleStack = result.RuleStack;
            TokenProcessor.ProcessTokensToParagraph(result.Tokens, lines[lineIndex], theme, paragraph);
            rows.Add(new Rows(paragraph));
        }
        return [.. rows];
    }

}
