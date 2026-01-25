using System.Text;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PSTextMate.Core;

/// <summary>
/// Provides optimized rendering for standard (non-Markdown) TextMate grammars.
/// Implements object pooling and batch processing for better performance.
/// </summary>
internal static class StandardRenderer {
    /// <summary>
    /// Renders text lines using standard TextMate grammar processing.
    /// Uses object pooling and batch processing for optimal performance.
    /// </summary>
    /// <param name="lines">Lines to render</param>
    /// <param name="theme">Theme to apply</param>
    /// <param name="grammar">Grammar for tokenization</param>
    /// <returns>Rendered rows with syntax highlighting</returns>
    // public static IRenderable[] Render(string[] lines, Theme theme, IGrammar grammar) => Render(lines, theme, grammar);

    public static IRenderable[] Render(string[] lines, Theme theme, IGrammar grammar) {
        List<IRenderable> rows = new(lines.Length);

        try {
            IStateStack? ruleStack = null;
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
                string line = lines[lineIndex];
                ITokenizeLineResult result = grammar.TokenizeLine(line, ruleStack, TimeSpan.MaxValue);
                ruleStack = result.RuleStack;

                if (string.IsNullOrEmpty(line)) {
                    rows.Add(Text.Empty);
                    continue;
                }

                var paragraph = new Paragraph();
                TokenProcessor.ProcessTokensToParagraph(result.Tokens, line, theme, paragraph);
                rows.Add(paragraph);
            }

            return [.. rows];
        }
        catch (ArgumentException ex) {
            throw new InvalidOperationException($"Argument error during rendering: {ex.Message}", ex);
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Unexpected error during rendering: {ex.Message}", ex);
        }
    }
}
