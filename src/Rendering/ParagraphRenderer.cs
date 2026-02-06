using System.Text;
using System.Text.RegularExpressions;
using Markdig.Extensions;
using Markdig.Extensions.AutoLinks;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PSTextMate.Core;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Themes;

namespace PSTextMate.Rendering;

/// <summary>
/// Paragraph renderer that builds Spectre.Console objects directly instead of markup strings.
/// Uses Text widgets instead of Paragraph to avoid extra spacing in terminal output.
/// This eliminates VT escaping issues and avoids double-parsing overhead.
/// </summary>
internal static partial class ParagraphRenderer {
    // reuse static arrays for common scope queries to avoid allocating new arrays per call
    private static readonly string[] LinkScope = ["markup.underline.link"];

    /// <summary>
    /// Renders a paragraph block by building Text objects with proper Style including link parameter.
    /// Avoids Paragraph widget spacing AND markup parsing overhead.
    /// Uses Style(foreground, background, decoration, link) for clickable links and styled code.
    /// </summary>
    /// <param name="paragraph">The paragraph block to render</param>
    /// <param name="theme">Theme for styling</param>
    /// <returns>Text segments with proper styling</returns>
    public static IEnumerable<IRenderable> Render(ParagraphBlock paragraph, Theme theme) {
        var segments = new List<IRenderable>();

        if (paragraph.Inline is not null) {
            BuildTextSegments(segments, paragraph.Inline, theme);
        }

        return segments;
    }

    /// <summary>
    /// Builds Text segments from inline elements with proper Style objects.
    /// Accumulates plain text and flushes when style changes (code, links).
    /// </summary>
    private static void BuildTextSegments(List<IRenderable> segments, ContainerInline inlines, Theme theme, bool skipLineBreaks = false) {
        var paragraph = new Paragraph();
        bool addedAny = false;

        List<Inline> inlineList = [.. inlines];

        for (int i = 0; i < inlineList.Count; i++) {
            Inline inline = inlineList[i];

            bool isTrailingLineBreak = false;
            if (inline is LineBreakInline && i < inlineList.Count) {
                isTrailingLineBreak = true;
                for (int j = i + 1; j < inlineList.Count; j++) {
                    if (inlineList[j] is not LineBreakInline) {
                        isTrailingLineBreak = false;
                        break;
                    }
                }
            }

            switch (inline) {
                case LiteralInline literal: {
                        string literalText = literal.Content.ToString();
                        if (!string.IsNullOrEmpty(literalText)) {
                            if (TryParseUsernameLinks(literalText, out TextSegment[]? usernameSegments)) {
                                foreach (TextSegment segment in usernameSegments) {
                                    if (segment.IsUsername) {
                                        var usernameStyle = new Style(Color.Blue, null, Decoration.Underline, $"https://github.com/{segment.Text.TrimStart('@')}");
                                        paragraph.Append(segment.Text, usernameStyle);
                                        addedAny = true;
                                    }
                                    else {
                                        paragraph.Append(segment.Text, Style.Plain);
                                        addedAny = true;
                                    }
                                }
                            }
                            else {
                                paragraph.Append(literalText, Style.Plain);
                                addedAny = true;
                            }
                        }
                    }
                    break;

                case EmphasisInline emphasis: {
                        Decoration decoration = GetEmphasisDecoration(emphasis.DelimiterCount);
                        var emphasisStyle = new Style(null, null, decoration);

                        foreach (Inline emphInline in emphasis) {
                            switch (emphInline) {
                                case LiteralInline lit:
                                    paragraph.Append(lit.Content.ToString(), emphasisStyle);
                                    addedAny = true;
                                    break;
                                case CodeInline codeInline:
                                    paragraph.Append(codeInline.Content, GetCodeStyle(theme));
                                    addedAny = true;
                                    break;
                                case LinkInline linkInline:
                                    // Build link style and include emphasis decoration
                                    string linkText = ExtractInlineText(linkInline);
                                    if (string.IsNullOrEmpty(linkText)) linkText = linkInline.Url ?? "";
                                    Style baseLink = GetLinkStyle(theme) ?? new Style(Color.Blue, null, Decoration.Underline);
                                    var combined = new Style(baseLink.Foreground, baseLink.Background, baseLink.Decoration | decoration | Decoration.Underline, linkInline.Url);
                                    paragraph.Append(linkText, combined);
                                    addedAny = true;
                                    break;
                                default:
                                    paragraph.Append(ExtractInlineText(emphInline), emphasisStyle);
                                    addedAny = true;
                                    break;
                            }
                        }
                    }
                    break;

                case CodeInline code:
                    paragraph.Append(code.Content, GetCodeStyle(theme));
                    addedAny = true;
                    break;

                case LinkInline link:
                    ProcessLinkAsText(paragraph, link, theme);
                    addedAny = true;
                    break;

                case AutolinkInline autoLink:
                    ProcessAutoLinkAsText(paragraph, autoLink, theme);
                    addedAny = true;
                    break;

                case LineBreakInline:
                    if (!skipLineBreaks && !isTrailingLineBreak) {
                        paragraph.Append("\n", Style.Plain);
                        addedAny = true;
                    }
                    break;

                case HtmlInline html:
                    paragraph.Append(html.Tag ?? "", Style.Plain);
                    addedAny = true;
                    break;

                case TaskList:
                    break;

                default:
                    paragraph.Append(ExtractInlineText(inline), Style.Plain);
                    addedAny = true;
                    break;
            }
        }

        if (addedAny) {
            segments.Add(paragraph);
        }
    }

    /// <summary>
    /// Process link as Text with Style including link parameter for clickability.
    /// </summary>
    private static void ProcessLinkAsText(Paragraph paragraph, LinkInline link, Theme theme) {
        if (link.IsImage) {
            string altText = ExtractInlineText(link);
            if (string.IsNullOrEmpty(altText)) altText = "Image";
            string imageLinkText = $"🖼️ {altText}";
            var style = new Style(Color.Blue, null, Decoration.Underline, link.Url);
            paragraph.Append(imageLinkText, style);
            return;
        }

        string linkText = ExtractInlineText(link);
        if (string.IsNullOrEmpty(linkText)) linkText = link.Url ?? "";

        Style linkStyle = GetLinkStyle(theme) ?? new Style(Color.Blue, null, Decoration.Underline);
        // Create new style with link parameter
        var styledLink = new Style(linkStyle.Foreground, linkStyle.Background, linkStyle.Decoration | Decoration.Underline, link.Url);
        paragraph.Append(linkText, styledLink);
    }

    /// <summary>
    /// Process autolink as Text with Style including link parameter.
    /// </summary>
    private static void ProcessAutoLinkAsText(Paragraph paragraph, AutolinkInline autoLink, Theme theme) {
        string url = autoLink.Url ?? "";
        if (string.IsNullOrEmpty(url)) return;

        Style linkStyle = GetLinkStyle(theme) ?? new Style(Color.Blue, null, Decoration.Underline);
        var styledLink = new Style(linkStyle.Foreground, linkStyle.Background, linkStyle.Decoration | Decoration.Underline, url);
        paragraph.Append(url, styledLink);
    }

    /// <summary>
    /// Get link style from theme.
    /// </summary>
    private static Style? GetLinkStyle(Theme theme)
        => TokenProcessor.GetStyleForScopes(LinkScope, theme);

    /// <summary>
    /// Get code style from theme.
    /// </summary>
    private static Style GetCodeStyle(Theme theme) {
        string[] codeScopes = ["markup.inline.raw"];
        (int codeFg, int codeBg, FontStyle codeFs) = TokenProcessor.ExtractThemeProperties(
            new MarkdownToken(codeScopes), theme);

        Color? foregroundColor = codeFg != -1 ? StyleHelper.GetColor(codeFg, theme) : Color.Yellow;
        Color? backgroundColor = codeBg != -1 ? StyleHelper.GetColor(codeBg, theme) : Color.Grey11;
        Decoration decoration = StyleHelper.GetDecoration(codeFs);

        return new Style(foregroundColor, backgroundColor, decoration);
    }

    /// <summary>
    /// Extracts plain text from inline elements without markup.
    /// </summary>
    private static string ExtractInlineText(Inline inline) {
        StringBuilder builder = StringBuilderPool.Rent();
        try {
            InlineTextExtractor.ExtractText(inline, builder);
            return builder.ToString();
        }
        finally {
            StringBuilderPool.Return(builder);
        }
    }

    /// <summary>
    /// Determine decoration to use for emphasis based on delimiter count and environment fallback.
    /// If environment variable `PSTEXTMATE_EMPHASIS_FALLBACK` == "underline" then use underline
    /// for single-asterisk emphasis so italics are visible on terminals that do not support italic.
    /// </summary>
    private static Decoration GetEmphasisDecoration(int delimiterCount) {
        // Read once per call; environment lookups are cheap here since rendering isn't hot inner loop
        string? fallback = Environment.GetEnvironmentVariable("PSTEXTMATE_EMPHASIS_FALLBACK");

        return delimiterCount switch {
            1 => string.Equals(fallback, "underline", StringComparison.OrdinalIgnoreCase) ? Decoration.Underline : Decoration.Italic,
            2 => Decoration.Bold,
            3 => Decoration.Bold | Decoration.Italic,
            _ => Decoration.None,
        };
    }

    /// <summary>
    /// Represents a text segment that may or may not be a username link.
    /// </summary>
    private sealed record TextSegment(string Text, bool IsUsername);

    /// <summary>
    /// Tries to parse username links (@username) from literal text.
    /// </summary>
    private static bool TryParseUsernameLinks(string text, out TextSegment[] segments) {
        var segmentList = new List<TextSegment>();

        // Simple regex to find @username patterns
        Regex usernamePattern = RegNumLet();
        MatchCollection matches = usernamePattern.Matches(text);

        if (matches.Count == 0) {
            segments = [];
            return false;
        }

        int lastIndex = 0;
        foreach (Match match in matches) {
            // Add text before the username
            if (match.Index > lastIndex) {
                segmentList.Add(new TextSegment(text[lastIndex..match.Index], false));
            }

            // Add the username
            segmentList.Add(new TextSegment(match.Value, true));
            lastIndex = match.Index + match.Length;
        }

        // Add remaining text
        if (lastIndex < text.Length) {
            segmentList.Add(new TextSegment(text[lastIndex..], false));
        }

        segments = [.. segmentList];
        return true;
    }



    [GeneratedRegex(@"@[a-zA-Z0-9_-]+")]
    private static partial Regex RegNumLet();
}
