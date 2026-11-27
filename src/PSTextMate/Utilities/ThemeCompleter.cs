namespace PSTextMate;

/// <summary>Completes built-in and runtime-registered theme names.</summary>
public sealed class ThemeCompleter : IArgumentCompleter {
    public IEnumerable<CompletionResult> CompleteArgument(string commandName, string parameterName,
        string wordToComplete, CommandAst commandAst, IDictionary fakeBoundParameters) {
        foreach (string name in HighlightingExtensions.ThemeNames) {
            if (name.StartsWith(wordToComplete ?? string.Empty, StringComparison.OrdinalIgnoreCase)) {
                yield return new CompletionResult("'" + name.Replace("'", "''", StringComparison.Ordinal) + "'", name,
                    CompletionResultType.ParameterValue, "TextMate theme");
            }
        }
    }
}
