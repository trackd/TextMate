using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PSTextMate;

/// <summary>
/// Argument completer for TextMate language IDs and file extensions in PowerShell commands.
/// </summary>
public sealed class LanguageCompleter : IArgumentCompleter {
    /// <summary>
    /// Offers completion for both TextMate language ids and file extensions.
    /// Examples: "powershell", "csharp", ".md", "md", ".ps1", "ps1".
    /// </summary>
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters) {
        string input = wordToComplete ?? string.Empty;
        bool wantsExtensionsOnly = input.Length > 0 && input[0] == '.';

        // Prefer wildcard matching semantics; fall back to prefix/contains when empty
        WildcardPattern? pattern = null;
        if (!string.IsNullOrEmpty(input)) {
            // Add trailing * if not already present to make incremental typing friendly
            string normalized = input[^1] == '*' ? input : input + "*";
            pattern = new WildcardPattern(normalized, WildcardOptions.IgnoreCase);
        }

        bool Match(string token) {
            if (pattern is null) return true; // no filter
            if (pattern.IsMatch(token)) return true;
            // Also test without a leading dot to match bare extensions like "ps1" against ".ps1"
            return token.StartsWith('.') && pattern.IsMatch(token[1..]);
        }

        // Build suggestions
        var results = new List<CompletionResult>();

        if (!wantsExtensionsOnly) {
            // Languages first
            foreach (string lang in TextMateHelper.Languages ?? []) {
                if (!Match(lang)) continue;
                results.Add(new CompletionResult(
                    completionText: lang,
                    listItemText: lang,
                    resultType: CompletionResultType.ParameterValue,
                    toolTip: "TextMate language"));
            }
        }

        // Extensions (always include if requested or no leading '.')
        foreach (string ext in TextMateHelper.Extensions ?? []) {
            if (!Match(ext)) continue;
            string completion = ext; // keep dot in completion
            string display = ext;
            results.Add(new CompletionResult(
                completionText: completion,
                listItemText: display,
                resultType: CompletionResultType.ParameterValue,
                toolTip: "File extension"));
        }

        // De-duplicate (in case of overlaps) and sort: languages first, then extensions, each alphabetically
        return results
            .GroupBy(r => r.CompletionText, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(r => r.ToolTip.Equals("TextMate language", StringComparison.Ordinal))
            .ThenBy(r => r.CompletionText, StringComparer.OrdinalIgnoreCase);
    }
}
/// <summary>
/// Provides validation for TextMate language IDs in parameter validation attributes.
/// </summary>
public class TextMateLanguages : IValidateSetValuesGenerator {
    /// <summary>
    /// Returns the list of all valid TextMate language IDs for parameter validation.
    /// </summary>
    /// <returns>Array of supported language identifiers</returns>
    public string[] GetValidValues() => TextMateHelper.Languages;
    /// <summary>
    /// Checks if a language ID is supported by TextMate.
    /// </summary>
    /// <param name="language">Language ID to validate</param>
    /// <returns>True if the language is supported, false otherwise</returns>
    public static bool IsSupportedLanguage(string language) => TextMateHelper.Languages.Contains(language);
}
/// <summary>
/// Provides validation for file extensions in parameter validation attributes.
/// </summary>
public class TextMateExtensions : IValidateSetValuesGenerator {
    /// <summary>
    /// Returns the list of all valid file extensions for parameter validation.
    /// </summary>
    /// <returns>Array of supported file extensions</returns>
    public string[] GetValidValues() => TextMateHelper.Extensions;
    /// <summary>
    /// Checks if a file extension is supported by TextMate.
    /// </summary>
    /// <param name="extension">File extension to validate (with or without dot)</param>
    /// <returns>True if the extension is supported, false otherwise</returns>
    public static bool IsSupportedExtension(string extension) => TextMateHelper.Extensions?.Contains(extension) == true;
    /// <summary>
    /// Checks if a file has a supported extension.
    /// </summary>
    /// <param name="file">File path to check</param>
    /// <returns>True if the file has a supported extension, false otherwise</returns>
    public static bool IsSupportedFile(string file) {
        string ext = Path.GetExtension(file);
        return TextMateHelper.Extensions?.Contains(ext) == true;
    }

}
/// <summary>
/// Argument transformer that normalizes file extensions to include a leading dot.
/// </summary>
public class TextMateExtensionTransform : ArgumentTransformationAttribute {
    /// <summary>
    /// Transforms an extension to include a leading dot if missing.
    /// </summary>
    /// <param name="engineIntrinsics">PowerShell engine intrinsics</param>
    /// <param name="inputData">Input string representing a file extension</param>
    /// <returns>Normalized extension with leading dot</returns>
    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData) {
        return inputData is string input
            ? (object)(input.StartsWith('.') ? input : '.' + input)
            : throw new ArgumentException("Input must be a string representing a file extension., '.ext' format expected.", nameof(inputData));
    }

}
