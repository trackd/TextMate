namespace PSTextMate.Utilities;

/// <summary>
/// Provides utility methods for accessing available TextMate languages and file extensions.
/// </summary>
public static class TextMateHelper {
    private static readonly SearchValues<char> NewLineChars = SearchValues.Create(['\r', '\n']);

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

        var lines = new List<string>(Math.Min(16, (input.Length / 8) + 1));
        AddSplitLines(lines, input, trimTrailingTerminatorEmptyLine: false);
        return [.. lines];
    }

    internal static string[] NormalizeToLines(List<string> buffer) {
        if (buffer.Count == 0) {
            return [];
        }

        var lines = new List<string>(buffer.Count * 2);
        foreach (string item in buffer) {
            AddSplitLines(lines, item, trimTrailingTerminatorEmptyLine: false);
        }

        return [.. lines];
    }

    internal static void AddSplitLines(List<string> destination, string input, bool trimTrailingTerminatorEmptyLine) {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(input);

        if (input.Length == 0) {
            destination.Add(string.Empty);
            return;
        }

        ReadOnlySpan<char> span = input.AsSpan();
        int lineStart = 0;

        while (lineStart <= span.Length) {
            int relativeBreak = span[lineStart..].IndexOfAny(NewLineChars);
            if (relativeBreak < 0) {
                destination.Add(new string(span[lineStart..]));
                break;
            }

            int breakIndex = lineStart + relativeBreak;
            destination.Add(new string(span[lineStart..breakIndex]));

            if (span[breakIndex] == '\r' && breakIndex + 1 < span.Length && span[breakIndex + 1] == '\n') {
                lineStart = breakIndex + 2;
            }
            else {
                lineStart = breakIndex + 1;
            }

            if (lineStart == span.Length) {
                destination.Add(string.Empty);
                break;
            }
        }

        if (trimTrailingTerminatorEmptyLine
            && destination.Count > 0
            && destination[^1].Length == 0
            && (span[^1] == '\n' || span[^1] == '\r')) {
            destination.RemoveAt(destination.Count - 1);
        }
    }
}
