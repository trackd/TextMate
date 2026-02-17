using System.Buffers;
using System.Text;
using Markdig.Helpers;
using Markdig.Syntax;
using PSTextMate.Core;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PSTextMate.Rendering;

/// <summary>
/// Code block renderer that builds Spectre.Console objects directly
/// and fixes whitespace and detection issues.
/// </summary>
internal static class CodeBlockRenderer {
    // Cached SearchValues for improved performance
    private static readonly SearchValues<char> LanguageDelimiters = SearchValues.Create([' ', '\t', '{', '}', '(', ')', '[', ']']);

    /// <summary>
    /// Renders a fenced code block with proper whitespace handling and language detection.
    /// </summary>
    /// <param name="fencedCode">The fenced code block to render</param>
    /// <param name="theme">Theme for styling</param>
    /// <param name="themeName">Theme name for TextMateProcessor</param>
    /// <returns>Rendered code block in a panel</returns>
    public static IRenderable RenderFencedCodeBlock(FencedCodeBlock fencedCode, Theme theme, ThemeName themeName) {
        string[] codeLines = ExtractCodeLinesWithWhitespaceHandling(fencedCode.Lines);
        string language = ExtractLanguage(fencedCode.Info);

        if (!string.IsNullOrEmpty(language)) {
            try {
                IRenderable[]? renderables = TextMateProcessor.ProcessLinesCodeBlock(codeLines, themeName, language, false);
                if (renderables is not null) {
                    return new Panel(new Rows(renderables))
                        .Border(BoxBorder.Rounded)
                        .Header(language, Justify.Left);
                }
            }
            catch {
                // Fallback to plain rendering
            }
        }

        // Fallback: create Text object directly instead of markup strings
        return CreateCodePanel(codeLines, language, theme);
    }    /// <summary>
         /// Renders an indented code block with proper whitespace handling.
         /// </summary>
         /// <param name="code">The code block to render</param>
         /// <param name="theme">Theme for styling</param>
         /// <returns>Rendered code block in a panel</returns>
    public static IRenderable RenderCodeBlock(CodeBlock code, Theme theme) {
        string[] codeLines = ExtractCodeLinesFromStringLineGroup(code.Lines);
        return CreateCodePanel(codeLines, "code", theme);
    }

    /// <summary>
    /// Extracts code lines with simple and safe processing to avoid bounds issues.
    /// </summary>
    private static string[] ExtractCodeLinesWithWhitespaceHandling(StringLineGroup lines) {
        if (lines.Count == 0)
            return [];

        var codeLines = new List<string>(lines.Count);

        foreach (StringLine line in lines.Lines) {
            try {
                // Use the safest approach: let the slice handle its own bounds
                string lineText = line.Slice.ToString();

                // Simple trailing whitespace trimming without spans
                lineText = lineText.TrimEnd();

                codeLines.Add(lineText);
            }
            catch {
                // If any error occurs, just use empty line
                codeLines.Add(string.Empty);
            }
        }

        // Convert to array and remove trailing empty lines
        return RemoveTrailingEmptyLines([.. codeLines]);
    }

    /// <summary>
    /// Extracts code lines from a string line group (for indented code blocks).
    /// </summary>
    private static string[] ExtractCodeLinesFromStringLineGroup(StringLineGroup lines) {
        if (lines.Count == 0)
            return [];

        string content = lines.ToString();
        if (string.IsNullOrEmpty(content))
            return [];

        // Split into lines and handle whitespace properly
        string[] splitLines = content.Split(['\r', '\n'], StringSplitOptions.None);

        // Process each line to handle whitespace correctly
        for (int i = 0; i < splitLines.Length; i++) {
            splitLines[i] = TrimTrailingWhitespace(splitLines[i].AsSpan()).ToString();
        }

        return RemoveTrailingEmptyLines(splitLines);
    }

    /// <summary>
    /// Improved language extraction with better detection patterns.
    /// </summary>
    private static string ExtractLanguage(string? info) {
        if (string.IsNullOrWhiteSpace(info))
            return string.Empty;

        ReadOnlySpan<char> infoSpan = info.AsSpan().Trim();

        // Handle various language specification formats
        // Examples: "csharp", "c#", "python copy", "javascript {1-3}", etc.

        // Find first whitespace or special character to extract just the language
        int endIndex = infoSpan.IndexOfAny(LanguageDelimiters);
        if (endIndex >= 0) {
            infoSpan = infoSpan[..endIndex];
        }

        string language = infoSpan.ToString().ToLowerInvariant();

        // Handle common language aliases and improve detection
        return NormalizeLanguageName(language);
    }

    /// <summary>
    /// Normalizes language names to improve code block detection.
    /// </summary>
    private static string NormalizeLanguageName(string language) {
        return language switch {
            "c#" or "csharp" or "cs" => "csharp",
            "js" or "javascript" => "javascript",
            "ts" or "typescript" => "typescript",
            "py" or "python" => "python",
            "ps1" or "powershell" or "pwsh" => "powershell",
            "sh" or "bash" => "bash",
            "yml" or "yaml" => "yaml",
            "md" or "markdown" => "markdown",
            "json" => "json",
            "xml" => "xml",
            "html" => "html",
            "css" => "css",
            "sql" => "sql",
            "dockerfile" => "dockerfile",
            _ => language
        };
    }

    /// <summary>
    /// Trims only trailing whitespace while preserving leading whitespace for indentation.
    /// </summary>
    private static ReadOnlySpan<char> TrimTrailingWhitespace(ReadOnlySpan<char> line) {
        int end = line.Length;
        while (end > 0 && char.IsWhiteSpace(line[end - 1])) {
            end--;
        }
        return line[..end];
    }

    /// <summary>
    /// Removes trailing empty lines that cause unnecessary whitespace in code blocks.
    /// </summary>
    private static string[] RemoveTrailingEmptyLines(string[] lines) {
        if (lines.Length == 0)
            return lines;

        int lastNonEmptyIndex = lines.Length - 1;

        // Find the last non-empty line
        while (lastNonEmptyIndex >= 0 && string.IsNullOrWhiteSpace(lines[lastNonEmptyIndex])) {
            lastNonEmptyIndex--;
        }

        // If all lines are empty, return a single empty line
        if (lastNonEmptyIndex < 0)
            return [string.Empty];

        // Return array up to the last non-empty line
        if (lastNonEmptyIndex == lines.Length - 1)
            return lines; // No trailing empty lines to remove

        string[] result = new string[lastNonEmptyIndex + 1];
        Array.Copy(lines, result, lastNonEmptyIndex + 1);
        return result;
    }

    /// <summary>
    /// Creates an optimized code panel using Text objects instead of markup strings.
    /// This eliminates VT escaping issues and improves performance.
    /// </summary>
    private static Panel CreateCodePanel(string[] codeLines, string language, Theme theme) {
        // Get theme colors for code blocks
        string[] codeScopes = ["text.html.markdown", "markup.fenced_code.block.markdown"];
        (int codeFg, int codeBg, FontStyle codeFs) = TokenProcessor.ExtractThemeProperties(
            new MarkdownToken(codeScopes), theme);

        // Create code styling
        Color? foregroundColor = codeFg != -1 ? StyleHelper.GetColor(codeFg, theme) : Color.Grey;
        Color? backgroundColor = codeBg != -1 ? StyleHelper.GetColor(codeBg, theme) : Color.Black;
        Decoration decoration = StyleHelper.GetDecoration(codeFs);
        var codeStyle = new Style(foregroundColor, backgroundColor, decoration);

        // Join lines efficiently
        string codeText = string.Join('\n', codeLines);

        // Create Text object directly instead of Markup to avoid parsing issues
        var codeContent = new Text(codeText, codeStyle);

        string headerText = !string.IsNullOrEmpty(language) ? language : "code";

        return new Panel(codeContent)
            .Border(BoxBorder.Rounded)
            .Header(headerText, Justify.Left);
    }
}
