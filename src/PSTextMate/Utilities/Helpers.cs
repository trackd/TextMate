namespace PSTextMate.Utilities;

/// <summary>
/// Provides utility methods for accessing available TextMate languages and file extensions.
/// </summary>
public static class TextMateHelper {
    private static readonly SearchValues<char> NewLineChars = SearchValues.Create(['\r', '\n']);

    /// <summary>
    /// Array of supported file extensions (e.g., ".ps1", ".md", ".cs").
    /// </summary>
    public static string[] Extensions => [.. GrammarCatalog.Instance.ByExtension.Keys.Order(StringComparer.OrdinalIgnoreCase)];
    public static string[] Languages => [.. GrammarCatalog.Instance.ByLanguage.Keys.Order(StringComparer.OrdinalIgnoreCase)];
    public static List<Language> AvailableLanguages => [.. GrammarCatalog.Instance.Languages];
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

            lineStart = span[breakIndex] == '\r' && breakIndex + 1 < span.Length && span[breakIndex + 1] == '\n' ? breakIndex + 2 : breakIndex + 1;

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
