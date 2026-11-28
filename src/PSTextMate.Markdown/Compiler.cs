using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PSTextMate.Core;

namespace PSTextMate.Markdown;

/// <summary>Shared Markdown content, styling and layout, without a UI framework.</summary>
public sealed partial class Compiler : IMarkdownPresentation {
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras().UseTaskLists().UsePipeTables().UseAutoLinks().Build();

    public HighlightedDocument Render(HighlightedDocument document, RendererOptions options) {
        var worker = new Worker(document, options);
        worker.Blocks(Markdig.Markdown.Parse(document.Source, Pipeline));
        return worker.Build();
    }

    [GeneratedRegex("<img\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageTag();
    [GeneratedRegex("(?<name>src|alt)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex ImageAttribute();

    private sealed class Worker {
        private readonly HighlightedDocument _original;
        private readonly RendererOptions _options;
        private readonly PresentedTextBuilder _output = new();
        private readonly ContentStyleResolver _styles;
        private readonly int _width;
        private bool _hasBlock;

        internal Worker(HighlightedDocument original, RendererOptions options) {
            _original = original;
            _options = options;
            _width = options.Width > 0 ? options.Width : 80;
            _styles = new ContentStyleResolver(original);
        }

        internal HighlightedDocument Build() => _output.Build(_original);

        private TextStyle Style(ContentRole role, TextDecoration decoration = TextDecoration.None, string? link = null) =>
            _styles.Resolve(role, decoration, link);

        internal void Blocks(ContainerBlock blocks, int indent = 0) {
            foreach (Block block in blocks) {
                if (_hasBlock) {
                    _output.NewLine();
                    _output.NewLine();
                }
                Block(block, indent);
                _hasBlock = true;
            }
        }

        private void Block(Block block, int indent) {
            indent = Math.Min(indent, Math.Max(0, _width - 4));
            switch (block) {
                case HeadingBlock heading:
                    WriteWrapped(InlineDocument(heading.Inline, Style((ContentRole)((int)ContentRole.Heading1 + Math.Clamp(heading.Level - 1, 0, 5)), TextDecoration.Bold)), indent);
                    break;
                case ParagraphBlock paragraph:
                    WriteWrapped(InlineDocument(paragraph.Inline, Style(ContentRole.Paragraph), indent), indent);
                    break;
                case FencedCodeBlock code:
                    ReadOnlySpan<char> info = code.Info.AsSpan().Trim(' ');
                    int separator = info.IndexOf(' ');
                    if (separator >= 0)
                        info = info[..separator];
                    Code(code, info.IsEmpty ? "code" : info.ToString(), indent);
                    break;
                case CodeBlock code:
                    Code(code, "code", indent);
                    break;
                case QuoteBlock quote:
                    var child = new Worker(_original, _options with { Width = Math.Max(8, _width - indent - 2) });
                    child.Blocks(quote);
                    Box(child.Build(), "quote", indent);
                    break;
                case ListBlock list:
                    List(list, indent);
                    break;
                case Table table:
                    Table(table, indent);
                    break;
                case ThematicBreakBlock:
                    _output.AppendRepeated('─', Math.Max(1, _width - indent), Style(ContentRole.Separator));
                    break;
                case HtmlBlock html:
                    Html(html.Lines.ToString(), indent);
                    break;
                case ContainerBlock container:
                    Blocks(container, indent);
                    break;
                case LeafBlock leaf:
                    WriteWrapped(InlineDocument(leaf.Inline, Style(ContentRole.Text)), indent);
                    break;
            }
        }

        private HighlightedDocument InlineDocument(ContainerInline? container, TextStyle style, int indent = 0) {
            var builder = new PresentedTextBuilder();
            if (container is not null)
                Inlines(container, builder, style, indent);
            return builder.Build(_original);
        }

        private RendererOptions ImageOptions(int localLines, int indent) {
            int viewport = _options.Height;
            if (viewport <= 0)
                viewport = !Console.IsOutputRedirected ? Math.Max(1, Console.WindowHeight - 2) : 30;
            int used = (_output.LineCount + localLines - 1) % viewport;
            int remaining = viewport - used;
            // Avoid reducing an image to an unreadable sliver at a page boundary.
            int height = remaining >= Math.Min(4, viewport) ? remaining : viewport;
            return _options with { Width = Math.Max(1, _width - indent), Height = height };
        }

        private void Inlines(ContainerInline container, PresentedTextBuilder builder, TextStyle style, int indent = 0) {
            foreach (Inline inline in container) {
                switch (inline) {
                    case LiteralInline literal:
                        builder.Append(literal.Content.AsSpan(), style);
                        break;
                    case CodeInline code:
                        builder.Append(code.Content, Style(ContentRole.InlineCode));
                        break;
                    case EmphasisInline emphasis:
                        TextDecoration decoration = emphasis.DelimiterChar == '~' ? TextDecoration.Strikethrough
                            : emphasis.DelimiterCount >= 2 ? TextDecoration.Bold : TextDecoration.Italic;
                        if (emphasis.DelimiterCount >= 3)
                            decoration |= TextDecoration.Italic;
                        ContentRole role = emphasis.DelimiterChar == '~' ? ContentRole.Text
                            : emphasis.DelimiterCount >= 3 ? ContentRole.BoldItalic
                            : emphasis.DelimiterCount >= 2 ? ContentRole.Bold : ContentRole.Italic;
                        TextStyle emphasisStyle = role == ContentRole.Text ? style : Style(role, link: style.Hyperlink);
                        Inlines(emphasis, builder, emphasisStyle with { Decoration = emphasisStyle.Decoration | style.Decoration | decoration }, indent);
                        break;
                    case LinkInline link when link.IsImage:
                        string alt = InlineDocument(link, style).Text;
                        builder.Append($"🖼️ Image: {alt}", Style(ContentRole.Image, TextDecoration.Underline, link.Url));
                        if (link.Url is { Length: > 0 } imageUrl && DocumentPresentation.Image(_original, imageUrl, ImageOptions(builder.LineCount, indent)) is { } image) {
                            builder.NewLine();
                            builder.Append(image);
                        }
                        break;
                    case LinkInline link:
                        Inlines(link, builder, Style(ContentRole.Link, TextDecoration.Underline, link.Url), indent);
                        break;
                    case AutolinkInline auto:
                        builder.Append(auto.Url, Style(ContentRole.Link, TextDecoration.Underline, auto.IsEmail ? "mailto:" + auto.Url : auto.Url));
                        break;
                    case LineBreakInline lineBreak:
                        builder.Append(lineBreak.IsHard ? "\n" : " ", style);
                        break;
                    case HtmlEntityInline entity:
                        builder.Append(entity.Transcoded.AsSpan(), style);
                        break;
                    case HtmlInline html:
                        if (html.Tag.Equals("<br>", StringComparison.OrdinalIgnoreCase)
                            || html.Tag.Equals("<br/>", StringComparison.OrdinalIgnoreCase)
                            || html.Tag.Equals("<br />", StringComparison.OrdinalIgnoreCase)) {
                            builder.NewLine();
                        }
                        else {
                            builder.Append(html.Tag, style);
                        }

                        break;
                    case TaskList:
                        break;
                    case ContainerInline child:
                        Inlines(child, builder, style, indent);
                        break;
                }
            }
        }

        private void WriteWrapped(HighlightedDocument text, int indent, string? prefix = null) {
            int columns = Math.Max(1, _width - indent);
            for (int lineIndex = 0; lineIndex < text.LineCount; lineIndex++) {
                HighlightedLine line = text.Lines.Span[lineIndex];
                int cursor = line.Start;
                int end = cursor + line.Length;
                bool first = true;
                do {
                    if (lineIndex > 0 || !first)
                        _output.NewLine();
                    _output.AppendRepeated(' ', indent, PresentedTextBuilder.Plain);
                    string marker = first && lineIndex == 0 ? prefix ?? "" : new string(' ', TerminalCells.Measure((prefix ?? "").AsSpan()));
                    if (TerminalCells.Measure(marker) >= columns) {
                        if (first && lineIndex == 0 && prefix is not null) {
                            _output.Append(marker.AsSpan(0, TerminalCells.Fit(marker, columns)), PresentedTextBuilder.Plain);
                            _output.NewLine();
                            _output.AppendRepeated(' ', indent, PresentedTextBuilder.Plain);
                        }
                        marker = string.Empty;
                    }
                    _output.Append(marker, PresentedTextBuilder.Plain);
                    int available = Math.Max(1, columns - TerminalCells.Measure(marker));
                    int count = TerminalCells.Fit(text.Text.AsSpan(cursor, end - cursor), available);
                    if (count == 0 && cursor < end)
                        count = StringInfo.GetNextTextElementLength(text.Text.AsSpan(cursor));
                    if (cursor + count < end) {
                        int space = text.Text.AsSpan(cursor, count).LastIndexOf(' ');
                        if (space > 0)
                            count = space;
                    }
                    _output.AppendSlice(text, cursor, count);
                    cursor += count;
                    while (cursor < end && text.Text[cursor] == ' ')
                        cursor++;
                    first = false;
                } while (cursor < end);
            }
        }

        private void Code(CodeBlock code, string language, int indent) {
            string content = code.Lines.ToString().TrimEnd('\r', '\n');
            HighlightedDocument highlighted;
            try {
                (string token, bool extension) = language.StartsWith('.') ? (language, true)
                    : GrammarCatalog.Instance.ByLanguage.ContainsKey(language) ? (language, false) : ("." + language, true);
                highlighted = SyntaxHighlighter.Highlight(content, token, _original.Theme, extension, false, _original.Catalog);
            }
            catch (ArgumentException) {
                highlighted = SyntaxHighlighter.Highlight(content, "plaintext", _original.Theme, false, false, _original.Catalog);
            }
            Box(highlighted, language, indent);
        }

        private void Box(HighlightedDocument content, string title, int indent, int minimumWidth = 0) {
            if (_width - indent < 4) {
                WriteWrapped(content, indent);
                return;
            }
            int contentWidth = Math.Max(TerminalCells.Measure(title) + 4, Math.Max(1, minimumWidth));
            foreach (HighlightedLine line in content.Lines.Span)
                contentWidth = Math.Max(contentWidth, TerminalCells.Measure(content.Text.AsSpan(line.Start, line.Length)));
            contentWidth = Math.Max(1, Math.Min(contentWidth, _width - indent - 2));
            TextStyle border = Style(ContentRole.CodeBlock);
            _output.AppendRepeated(' ', indent, border);
            _output.Append("╭", border);
            string label = "─" + title;
            int titleChars = TerminalCells.Fit(label, contentWidth);
            _output.Append(label.AsSpan(0, titleChars), border);
            _output.AppendRepeated('─', Math.Max(0, contentWidth - TerminalCells.Measure(label.AsSpan(0, titleChars))), border);
            _output.Append("╮", border);
            foreach (HighlightedLine line in content.Lines.Span) {
                int cursor = line.Start;
                int end = cursor + line.Length;
                do {
                    _output.NewLine();
                    _output.AppendRepeated(' ', indent, border);
                    _output.Append("│", border);
                    int count = TerminalCells.Fit(content.Text.AsSpan(cursor, end - cursor), contentWidth);
                    if (count == 0 && cursor < end)
                        count = StringInfo.GetNextTextElementLength(content.Text.AsSpan(cursor));
                    _output.AppendSlice(content, cursor, count);
                    int padding = Math.Max(0, contentWidth - TerminalCells.Measure(content.Text.AsSpan(cursor, count)));
                    _output.AppendRepeated(' ', padding, border);
                    _output.Append("│", border);
                    cursor += count;
                } while (cursor < end);
            }
            _output.NewLine();
            _output.AppendRepeated(' ', indent, border);
            _output.Append("╰", border);
            _output.AppendRepeated('─', contentWidth, border);
            _output.Append("╯", border);
        }

        private void List(ListBlock list, int indent) {
            int number = int.TryParse(list.OrderedStart, CultureInfo.InvariantCulture, out int start) ? start : 1;
            bool firstItem = true;
            foreach (ListItemBlock item in list.Cast<ListItemBlock>()) {
                if (!firstItem)
                    _output.NewLine();
                bool firstBlock = true;
                foreach (Block block in item) {
                    if (!firstBlock)
                        _output.NewLine();
                    if (block is ParagraphBlock paragraph) {
                        string marker = list.IsOrdered ? $"{number}. " : "• ";
                        if (paragraph.Inline?.FirstChild is TaskList task)
                            marker = task.Checked ? "✅ " : "⬜ ";
                        WriteWrapped(InlineDocument(paragraph.Inline, Style(ContentRole.List), indent + 2), indent, firstBlock ? marker : "  ");
                    }
                    else {
                        Block(block, indent + 4);
                    }

                    firstBlock = false;
                }
                number++;
                firstItem = false;
            }
        }

        private void Table(Table table, int indent) {
            int columns = table.ColumnDefinitions.Count;
            if (columns == 0)
                return;
            var rows = new List<HighlightedDocument[]>();
            int[] widths = new int[columns];
            foreach (TableRow row in table.Cast<TableRow>()) {
                var cells = new HighlightedDocument[columns];
                for (int column = 0; column < columns; column++) {
                    var builder = new PresentedTextBuilder();
                    if (column < row.Count && row[column] is TableCell cell) {
                        foreach (Block block in cell) {
                            if (builder.Length > 0)
                                builder.Append(" ", PresentedTextBuilder.Plain);
                            if (block is ParagraphBlock paragraph)
                                builder.Append(InlineDocument(paragraph.Inline, Style(ContentRole.TableCell, row.IsHeader ? TextDecoration.Bold : TextDecoration.None)));
                        }
                    }
                    cells[column] = builder.Build(_original);
                    foreach (HighlightedLine line in cells[column].Lines.Span)
                        widths[column] = Math.Max(widths[column], TerminalCells.Measure(cells[column].Text.AsSpan(line.Start, line.Length)));
                }
                rows.Add(cells);
            }
            if ((columns * 4) + 1 > _width - indent) {
                bool first = true;
                foreach (HighlightedDocument[] row in rows) {
                    foreach (HighlightedDocument cell in row) {
                        if (!first)
                            _output.NewLine();
                        WriteWrapped(cell, indent);
                        first = false;
                    }
                }
                return;
            }
            int available = Math.Max(columns, _width - indent - (columns * 3) - 1);
            int totalWidth = widths.Sum();
            while (totalWidth > available) {
                int widest = 0;
                for (int i = 1; i < columns; i++) {
                    if (widths[i] > widths[widest])
                        widest = i;
                }

                if (widths[widest] <= 1)
                    break;
                widths[widest]--;
                totalWidth--;
            }
            TextStyle border = Style(ContentRole.Table);
            void Rule(char left, char middle, char right) {
                _output.Append(new string(' ', indent) + left, border);
                for (int i = 0; i < columns; i++)
                    _output.Append(new string('─', widths[i] + 2) + (i + 1 < columns ? middle : right), border);
            }
            Rule('╭', '┬', '╮');
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++) {
                int[] offsets = new int[columns];
                bool remaining;
                do {
                    remaining = false;
                    _output.NewLine();
                    _output.AppendRepeated(' ', indent, border);
                    _output.Append("│", border);
                    for (int column = 0; column < columns; column++) {
                        HighlightedDocument cell = rows[rowIndex][column];
                        int start = offsets[column];
                        int newline = cell.Text.AsSpan(start).IndexOf('\n');
                        int end = newline < 0 ? cell.Text.Length : start + newline;
                        int count = TerminalCells.Fit(cell.Text.AsSpan(start, end - start), widths[column]);
                        if (count == 0 && start < end)
                            count = StringInfo.GetNextTextElementLength(cell.Text.AsSpan(start, end - start));
                        if (start + count < end) {
                            int space = cell.Text.AsSpan(start, count).LastIndexOf(' ');
                            if (space > 0)
                                count = space;
                        }
                        int padding = Math.Max(0, widths[column] - TerminalCells.Measure(cell.Text.AsSpan(offsets[column], count)));
                        TableColumnAlign? alignment = table.ColumnDefinitions[column].Alignment;
                        int left = alignment == TableColumnAlign.Right ? padding : alignment == TableColumnAlign.Center ? padding / 2 : 0;
                        _output.AppendRepeated(' ', left + 1, PresentedTextBuilder.Plain);
                        _output.AppendSlice(cell, offsets[column], count);
                        _output.AppendRepeated(' ', padding - left + 1, border);
                        _output.Append("│", border);
                        offsets[column] += count;
                        while (offsets[column] < end && cell.Text[offsets[column]] == ' ')
                            offsets[column]++;
                        if (offsets[column] == end && end < cell.Text.Length)
                            offsets[column]++;
                        remaining |= offsets[column] < cell.Text.Length;
                    }
                } while (remaining);
                if (rowIndex == 0 || rowIndex + 1 == rows.Count) {
                    _output.NewLine();
                    Rule(rowIndex + 1 == rows.Count ? '╰' : '├', rowIndex + 1 == rows.Count ? '┴' : '┼', rowIndex + 1 == rows.Count ? '╯' : '┤');
                }
            }
        }

        private void Html(string html, int indent) {
            bool first = true;
            foreach (Match image in ImageTag().Matches(html)) {
                if (!first)
                    _output.NewLine();
                string alt = "image", source = "";
                foreach (Match attribute in ImageAttribute().Matches(image.Value)) {
                    if (attribute.Groups["name"].Value.Equals("alt", StringComparison.OrdinalIgnoreCase))
                        alt = attribute.Groups["value"].Value;
                    else
                        source = attribute.Groups["value"].Value;
                }
                var text = new PresentedTextBuilder();
                text.Append("🖼️ Image: " + alt, Style(ContentRole.Image, TextDecoration.Underline, source));
                if (DocumentPresentation.Image(_original, source, ImageOptions(text.LineCount, indent)) is { } picture) {
                    text.NewLine();
                    text.Append(picture);
                }
                WriteWrapped(text.Build(_original), indent);
                first = false;
            }
            if (first)
                Box(SyntaxHighlighter.Highlight(html, "html", _original.Theme, false, false, _original.Catalog), "html", indent);
        }
    }
}
