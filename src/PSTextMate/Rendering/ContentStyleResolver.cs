using Tint.Themes;

namespace PSTextMate.Core;

/// <summary>Semantic content roles shared by every output representation.</summary>
public enum ContentRole {
    Text, Heading1, Heading2, Heading3, Heading4, Heading5, Heading6,
    Paragraph, List, Table, TableRow, TableCell, Quote, Separator,
    CodeBlock, HtmlBlock, Task, Italic, Bold, BoldItalic, Link, Image, InlineCode, Whitespace
}

/// <summary>Resolves semantic roles through TextMate theme scopes once, before output conversion.</summary>
internal sealed class ContentStyleResolver(HighlightedDocument document) {
    private static readonly string[][] Scopes = [
        ["text.plain"],
        ["markup.heading.1.markdown", "markup.heading.markdown"],
        ["markup.heading.2.markdown", "markup.heading.markdown"],
        ["markup.heading.3.markdown", "markup.heading.markdown"],
        ["markup.heading.4.markdown", "markup.heading.markdown"],
        ["markup.heading.5.markdown", "markup.heading.markdown"],
        ["markup.heading.6.markdown", "markup.heading.markdown"],
        ["markup.paragraph.markdown", "text.plain"], ["markup.list.markdown"],
        ["markup.table.markdown"], ["markup.table.row.markdown"], ["markup.table.cell.markdown"],
        ["markup.quote.markdown"], ["meta.separator.markdown"], ["markup.raw.block.markdown"],
        ["markup.raw.block.html.markdown"], ["markup.list.task.markdown"], ["markup.italic.markdown"],
        ["markup.bold.markdown"], ["markup.bold.markdown", "markup.italic.markdown"],
        ["markup.underline.link.markdown"], ["markup.underline.link.image.markdown"],
        ["markup.inline.raw.markdown"], ["text.whitespace"]
    ];
    private readonly Tint.Theme _theme = SyntaxHighlighter.GetTheme(document.Theme, document.Catalog);
    private readonly TextStyle[] _styles = new TextStyle[Scopes.Length];
    private readonly bool[] _resolved = new bool[Scopes.Length];

    internal TextStyle Resolve(ContentRole role, TextDecoration decoration = TextDecoration.None, string? link = null) {
        int index = (int)role;
        if ((uint)index >= Scopes.Length)
            throw new ArgumentOutOfRangeException(nameof(role));
        if (!_resolved[index]) {
            StyleAttributes? match = null;
            foreach (string scope in Scopes[index]) {
                match = _theme.Match(["text.html.markdown", scope]);
                if (match is not null)
                    break;
            }
            StyleAttributes attributes = match ?? _theme.Defaults;
            TextDecoration font = attributes.FontStyle == Tint.FontStyle.NotSet ? TextDecoration.None : (TextDecoration)attributes.FontStyle;
            _styles[index] = new(attributes.ForegroundId <= 0 || attributes.ForegroundId == _theme.Defaults.ForegroundId ? -1 : Color(attributes.ForegroundId),
                attributes.BackgroundId <= 0 || attributes.BackgroundId == _theme.Defaults.BackgroundId ? -1 : Color(attributes.BackgroundId), font);
            _resolved[index] = true;
        }
        if (link is not null) {
            foreach (char character in link) {
                if (char.IsControl(character)) {
                    link = null;
                    break;
                }
            }
        }
        TextStyle style = _styles[index];
        return style with { Decoration = style.Decoration | decoration, Hyperlink = link };
    }

    private int Color(int id) {
        string color = _theme.ColorMap.GetColor(id);
        return color.Length >= 7 ? int.Parse(color.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : -1;
    }
}
