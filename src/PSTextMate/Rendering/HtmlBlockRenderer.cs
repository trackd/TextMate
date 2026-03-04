using Markdig.Syntax;
using PSTextMate.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.IO;
using System.Text.RegularExpressions;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PSTextMate.Rendering;

/// <summary>
/// Renders HTML blocks with syntax highlighting.
/// </summary>
internal static partial class HtmlBlockRenderer {
    /// <summary>
    /// Renders an HTML block with syntax highlighting when possible.
    /// </summary>
    /// <param name="htmlBlock">The HTML block to render</param>
    /// <param name="theme">Theme for styling</param>
    /// <param name="themeName">Theme name for TextMateProcessor</param>
    /// <returns>Rendered HTML block in a panel</returns>
    public static IRenderable Render(HtmlBlock htmlBlock, Theme theme, ThemeName themeName) {
        List<string> htmlLines = ExtractHtmlLines(htmlBlock);

        // Handle standalone HTML <img> tags as real images (Sixel/fallback), not code blocks.
        if (TryExtractImageTag(htmlLines, out HtmlImageTag? imageTag) && imageTag is not null) {
            return ImageRenderer.RenderImage(imageTag.AltText, imageTag.Source, imageTag.Width, imageTag.Height);
        }

        // Try to render with HTML syntax highlighting
        try {
            IRenderable[]? htmlRenderables = TextMateProcessor.ProcessLinesCodeBlock([.. htmlLines], themeName, "html", false);
            if (htmlRenderables is not null) {
                return new Panel(new Rows(htmlRenderables))
                    .Border(BoxBorder.Rounded)
                    .Header("html", Justify.Left);
            }
        }
        catch {
            // Fallback to plain rendering
        }

        // Fallback: plain HTML panel
        return CreateFallbackHtmlPanel(htmlLines);
    }

    /// <summary>
    /// Extracts HTML lines from the HTML block.
    /// </summary>
    private static List<string> ExtractHtmlLines(HtmlBlock htmlBlock) {
        var htmlLines = new List<string>();

        for (int i = 0; i < htmlBlock.Lines.Count; i++) {
            Markdig.Helpers.StringLine line = htmlBlock.Lines.Lines[i];
            htmlLines.Add(line.Slice.ToString());
        }

        return htmlLines;
    }

    private static bool TryExtractImageTag(List<string> htmlLines, out HtmlImageTag? imageTag) {
        imageTag = null;

        string html = string.Join(" ", htmlLines).Trim();
        if (string.IsNullOrWhiteSpace(html)) {
            return false;
        }

        Match imgMatch = HtmlImageTagRegex().Match(html);

        if (!imgMatch.Success) {
            return false;
        }

        string attributes = imgMatch.Groups["attrs"].Value;
        string? src = ExtractAttribute(attributes, "src");
        if (string.IsNullOrWhiteSpace(src)) {
            return false;
        }

        string? alt = ExtractAttribute(attributes, "alt");
        string fallbackAlt = Path.GetFileNameWithoutExtension(src) ?? "Image";
        int? width = ParseDimension(ExtractAttribute(attributes, "width"));
        int? height = ParseDimension(ExtractAttribute(attributes, "height"));

        imageTag = new HtmlImageTag(src, string.IsNullOrWhiteSpace(alt) ? fallbackAlt : alt, width, height);
        return true;
    }

    private static string? ExtractAttribute(string attributes, string attributeName) {
        MatchCollection matches = HtmlAttributeRegex().Matches(attributes);
        foreach (Match match in matches) {
            string name = match.Groups["name"].Value;
            if (string.Equals(name, attributeName, StringComparison.OrdinalIgnoreCase)) {
                return match.Groups["value"].Value;
            }
        }

        return null;
    }

    private static int? ParseDimension(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        Match digits = DimensionDigitsRegex().Match(value);
        if (!digits.Success) {
            return null;
        }

        return int.TryParse(digits.Value, out int parsed) && parsed > 0
            ? parsed
            : null;
    }

    private sealed record HtmlImageTag(string Source, string AltText, int? Width, int? Height);

    [GeneratedRegex(@"^\s*<img\b(?<attrs>[^>]*)\/?>\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlImageTagRegex();

    [GeneratedRegex(@"\b(?<name>[\w:-]+)\s*=\s*(?:\""(?<value>[^\""]*)\""|'(?<value>[^']*)'|(?<value>[^\s\""'>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlAttributeRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex DimensionDigitsRegex();

    /// <summary>
    /// Creates a fallback HTML panel when syntax highlighting fails.
    /// </summary>
    private static Panel CreateFallbackHtmlPanel(List<string> htmlLines) {
        string htmlText = string.Join("\n", htmlLines);
        var text = new Text(htmlText, Style.Plain);
        return new Panel(text)
            .Border(BoxBorder.Rounded)
            .Header("html", Justify.Left);
    }
}
