using System.Text;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PSTextMate.Core;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Themes;

namespace PSTextMate.Rendering;

/// <summary>
/// Table renderer that builds Spectre.Console objects directly instead of markup strings.
/// This eliminates VT escaping issues and provides proper color support.
/// </summary>
internal static class TableRenderer {
    /// <summary>
    /// Renders a markdown table by building Spectre.Console Table objects directly.
    /// This approach provides proper theme color support and eliminates VT escaping issues.
    /// </summary>
    /// <param name="table">The table block to render</param>
    /// <param name="theme">Theme for styling</param>
    /// <returns>Rendered table with proper styling</returns>
    public static IRenderable? Render(Markdig.Extensions.Tables.Table table, Theme theme) {
        var spectreTable = new Spectre.Console.Table {
            ShowFooters = false,

            // Configure table appearance
            Border = TableBorder.Rounded,
            BorderStyle = GetTableBorderStyle(theme)
        };

        List<(bool isHeader, List<TableCellContent> cells)> allRows = ExtractTableDataOptimized(table, theme);

        if (allRows.Count == 0)
            return null;

        // Add headers if present
        (bool isHeader, List<TableCellContent> cells) headerRow = allRows.FirstOrDefault(r => r.isHeader);
        if (headerRow.cells?.Count > 0) {
            for (int i = 0; i < headerRow.cells.Count; i++) {
                TableCellContent cell = headerRow.cells[i];
                // Use constructor to set header text; this is the most compatible way
                var column = new TableColumn(cell.Text);
                // Apply alignment if Markdig specified one for the column
                if (i < table.ColumnDefinitions.Count) {
                    column.Alignment = table.ColumnDefinitions[i].Alignment switch {
                        TableColumnAlign.Left => Justify.Left,
                        TableColumnAlign.Center => Justify.Center,
                        TableColumnAlign.Right => Justify.Right,
                        _ => Justify.Left
                    };
                }
                spectreTable.AddColumn(column);
            }
        }
        else {
            // No explicit headers, use first row as headers
            (bool isHeader, List<TableCellContent> cells) = allRows.FirstOrDefault();
            if (cells?.Count > 0) {
                for (int i = 0; i < cells.Count; i++) {
                    TableCellContent cell = cells[i];
                    var column = new TableColumn(cell.Text);
                    if (i < table.ColumnDefinitions.Count) {
                        column.Alignment = table.ColumnDefinitions[i].Alignment switch {
                            TableColumnAlign.Left => Justify.Left,
                            TableColumnAlign.Center => Justify.Center,
                            TableColumnAlign.Right => Justify.Right,
                            _ => Justify.Left
                        };
                    }
                    spectreTable.AddColumn(column);
                }
                allRows = [.. allRows.Skip(1)];
            }
        }

        // Add data rows
        foreach ((bool isHeader, List<TableCellContent>? cells) in allRows.Where(r => !r.isHeader)) {
            if (cells?.Count > 0) {
                var rowCells = new List<IRenderable>();
                foreach (TableCellContent? cell in cells) {
                    Style cellStyle = GetCellStyle(theme);
                    rowCells.Add(new Text(cell.Text, cellStyle));
                }
                spectreTable.AddRow(rowCells.ToArray());
            }
        }

        return spectreTable;
    }

    /// <summary>
    /// Represents the content and styling of a table cell.
    /// </summary>
    internal sealed record TableCellContent(string Text, TableColumnAlign? Alignment);

    /// <summary>
    /// Extracts table data with optimized cell content processing.
    /// </summary>
    internal static List<(bool isHeader, List<TableCellContent> cells)> ExtractTableDataOptimized(
        Markdig.Extensions.Tables.Table table, Theme theme) {
        var result = new List<(bool isHeader, List<TableCellContent> cells)>();

        foreach (Markdig.Extensions.Tables.TableRow row in table.Cast<Markdig.Extensions.Tables.TableRow>()) {
            bool isHeader = row.IsHeader;
            var cells = new List<TableCellContent>();

            for (int i = 0; i < row.Count; i++) {
                if (row[i] is TableCell cell) {
                    string cellText = ExtractCellTextOptimized(cell, theme);
                    TableColumnAlign? alignment = i < table.ColumnDefinitions.Count ? table.ColumnDefinitions[i].Alignment : null;
                    cells.Add(new TableCellContent(cellText, alignment));
                }
            }

            result.Add((isHeader, cells));
        }

        return result;
    }

    /// <summary>
    /// Extracts text from table cells using optimized inline processing.
    /// </summary>
    private static string ExtractCellTextOptimized(TableCell cell, Theme theme) {
        StringBuilder textBuilder = StringBuilderPool.Rent();

        foreach (Block block in cell) {
            if (block is ParagraphBlock paragraph && paragraph.Inline is not null) {
                ExtractInlineTextOptimized(paragraph.Inline, textBuilder);
            }
            else if (block is CodeBlock code) {
                textBuilder.Append(code.Lines.ToString());
            }
        }

        string result = textBuilder.ToString().Trim();
        StringBuilderPool.Return(textBuilder);
        return result;
    }

    /// <summary>
    /// Extracts text from inline elements optimized for table cells.
    /// </summary>
    private static void ExtractInlineTextOptimized(ContainerInline inlines, StringBuilder builder) {
        // Small optimization: use a borrowed buffer for frequently accessed literal content instead of repeated ToString allocations.
        foreach (Inline inline in inlines) {
            switch (inline) {
                case LiteralInline literal:
                    // Append span directly from the underlying string to avoid creating intermediate allocations
                    StringSlice slice = literal.Content;
                    if (slice.Text is not null && slice.Length > 0) {
                        builder.Append(slice.Text.AsSpan(slice.Start, slice.Length));
                    }
                    break;

                case EmphasisInline emphasis:
                    InlineTextExtractor.ExtractText(emphasis, builder);
                    break;

                case CodeInline code:
                    builder.Append(code.Content);
                    break;

                case LinkInline link:
                    InlineTextExtractor.ExtractText(link, builder);
                    break;

                default:
                    InlineTextExtractor.ExtractText(inline, builder);
                    break;
            }
        }
    }



    /// <summary>
    /// Gets the border style for tables based on theme.
    /// </summary>
    private static Style GetTableBorderStyle(Theme theme) {
        string[] borderScopes = ["punctuation.definition.table"];
        Style? style = TokenProcessor.GetStyleForScopes(borderScopes, theme);
        return style is not null ? style : new Style(foreground: Color.Grey);
    }

    /// <summary>
    /// Gets the header style for table headers.
    /// </summary>
    private static Style GetHeaderStyle(Theme theme) {
        string[] headerScopes = ["markup.heading.table"];
        Style? baseStyle = TokenProcessor.GetStyleForScopes(headerScopes, theme);
        Color fgColor = baseStyle?.Foreground ?? Color.Yellow;
        Color? bgColor = baseStyle?.Background;
        Decoration decoration = (baseStyle is not null ? baseStyle.Decoration : Decoration.None) | Decoration.Bold;
        return new Style(fgColor, bgColor, decoration);
    }

    /// <summary>
    /// Gets the cell style for table data cells.
    /// </summary>
    private static Style GetCellStyle(Theme theme) {
        string[] cellScopes = ["markup.table.cell"];
        Style? baseStyle = TokenProcessor.GetStyleForScopes(cellScopes, theme);
        Color fgColor = baseStyle?.Foreground ?? Color.White;
        Color? bgColor = baseStyle?.Background;
        Decoration decoration = baseStyle?.Decoration ?? Decoration.None;
        return new Style(fgColor, bgColor, decoration);
    }
}
