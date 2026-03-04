using TextMateSharp.Grammars;

namespace PSTextMate;

/// <summary>
/// Provides utility methods for accessing available TextMate languages and file extensions.
/// </summary>
public static class TextMateHelper {
    /// <summary>
    /// Array of supported file extensions (e.g., ".ps1", ".md", ".cs").
    /// </summary>
    public static readonly string[] Extensions;
    /// <summary>
    /// Array of supported TextMate language identifiers (e.g., "powershell", "markdown", "csharp").
    /// </summary>
    public static readonly string[] Languages;
    /// <summary>
    /// List of all available language definitions with metadata.
    /// </summary>
    public static readonly List<Language> AvailableLanguages;
    static TextMateHelper() {
        try {
            RegistryOptions _registryOptions = new(ThemeName.DarkPlus);
            AvailableLanguages = _registryOptions.GetAvailableLanguages();

            // Get all the extensions and languages from the available languages
            Extensions = [.. AvailableLanguages
                .Where(x => x.Extensions is not null)
                .SelectMany(x => x.Extensions)];

            Languages = [.. AvailableLanguages
                .Where(x => x.Id is not null)
                .Select(x => x.Id)];
        }
        catch (Exception ex) {
            throw new TypeInitializationException(nameof(TextMateHelper), ex);
        }
    }
    internal static string[] SplitToLines(string input) {
        if (input.Length == 0) {
            return [string.Empty];
        }

        if (input.Contains('\n') || input.Contains('\r')) {
            return input.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        }

        return [input];
    }

    internal static string[] NormalizeToLines(List<string> buffer) {
        if (buffer.Count == 0) {
            return [];
        }

        var lines = new List<string>(buffer.Count * 2);
        foreach (string item in buffer) {
            lines.AddRange(SplitToLines(item));
        }

        return [.. lines];
    }
}
