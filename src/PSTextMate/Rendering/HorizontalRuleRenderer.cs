namespace PSTextMate.Rendering;

/// <summary>
/// Renders markdown horizontal rules (thematic breaks).
/// </summary>
internal static class HorizontalRuleRenderer {
    private const int HorizontalInset = 5;

    /// <summary>
    /// Renders a horizontal rule as a styled line.
    /// </summary>
    /// <returns>Rendered horizontal rule</returns>
    public static IRenderable Render()
        // Keep some side room so thematic breaks do not look edge-to-edge,
        // especially when line numbers or other gutters are enabled.
        => new Padder(
            new Rule().RuleStyle(new Style(Color.Gray)),
            new Padding(0, 0, HorizontalInset, 0)
        );
}
