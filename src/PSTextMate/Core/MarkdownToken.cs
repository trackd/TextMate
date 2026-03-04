using TextMateSharp.Grammars;

namespace PSTextMate.Core;

/// <summary>
/// Simple token for theme lookup from a set of scopes (for markdown elements).
/// </summary>
internal sealed class MarkdownToken(IEnumerable<string> scopes) : IToken {
    public string Text { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public int Length { get; set; }
    public List<string> Scopes { get; } = [.. scopes];
}
