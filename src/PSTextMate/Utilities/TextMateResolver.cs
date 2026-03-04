using System;

namespace PSTextMate;

/// <summary>
/// Resolves a user-provided token into either a TextMate language id or a file extension.
/// </summary>
internal static class TextMateResolver {
    /// <summary>
    /// Resolve a grammar token that may be a language id or a file extension.
    /// Heuristics:
    /// - If starts with '.', treat as extension
    /// - If known TextMate language id, treat as language
    /// - Otherwise treat as extension (allow values like 'ps1', 'md')
    /// </summary>
    public static (string token, bool asExtension) ResolveToken(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return ("powershell", false);
        }

        string v = value.Trim();
        if (v.StartsWith('.')) {
            return (v, true);
        }

        if (TextMateLanguages.IsSupportedLanguage(v)) {
            return (v, false);
        }

        // Treat anything else as an extension; prefix a dot if missing
        return (v.StartsWith('.') ? v : "." + v, true);
    }
}
