namespace PSTextMate.Core;

/// <summary>Direct terminal output from styled ranges, with no layout-framework dependency.</summary>
public static partial class TextRenderer {
    public static void Write(HighlightedDocument document, TextWriter writer, bool ansi = true,
        int startLine = 0, int count = int.MaxValue, bool graphics = false) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);
        if (ReferenceEquals(writer, Console.Out)) {
            var buffer = TextBufferWriter.Rent();
            try {
                buffer.Attach(writer);
                Write(document, buffer, ansi, startLine, count, graphics);
                buffer.Flush();
            }
            finally {
                TextBufferWriter.Return(buffer);
            }
            return;
        }
        ReadOnlySpan<HighlightedLine> lines = document.Lines.Span;
        ReadOnlySpan<StyledRange> ranges = document.Ranges.Span;
        int firstLine = Math.Clamp(startLine, 0, lines.Length);
        int lastLine = firstLine + Math.Min(Math.Max(0, count), lines.Length - firstLine);
        bool drawImages = graphics && ansi && !document.Images.IsEmpty;
        for (int lineIndex = firstLine; lineIndex < lastLine; lineIndex++) {
            HighlightedLine line = lines[lineIndex];
            if (document.ShowLineNumbers) {
                LineNumberGutter.Write(writer, document.GetLineNumber(lineIndex), document.LineNumberTotal);
            }
            int cursor = line.Start;
            for (int index = line.FirstRange; index < line.FirstRange + line.RangeCount; index++) {
                StyledRange range = ranges[index];
                if (range.Start > cursor) {
                    if (drawImages)
                        WriteImageCells(document, writer, lineIndex, cursor, range.Start - cursor, TextStyle.Plain, firstLine, lastLine);
                    else
                        TerminalText.WriteSafe(writer, document.Text.AsSpan(cursor, range.Start - cursor));
                }
                if (drawImages)
                    WriteImageCells(document, writer, lineIndex, range.Start, range.Length, range.Style, firstLine, lastLine);
                else
                    TextOutput.Write(writer, document.Text.AsSpan(range.Start, range.Length), range.Style, ansi);
                cursor = range.Start + range.Length;
            }
            int end = line.Start + line.Length;
            if (cursor < end) {
                if (drawImages)
                    WriteImageCells(document, writer, lineIndex, cursor, end - cursor, TextStyle.Plain, firstLine, lastLine);
                else
                    TerminalText.WriteSafe(writer, document.Text.AsSpan(cursor, end - cursor));
            }
            int next = lineIndex + 1 < lines.Length ? lines[lineIndex + 1].Start : document.Text.Length;
            writer.Write(document.Text.AsSpan(end, next - end));
            if (drawImages)
                WriteCompletedImages(document, writer, lineIndex, next > end, firstLine, lastLine);
        }
    }

    /// <summary>PowerShell formatting entry point; does not write directly to the host.</summary>
    public static string Format(HighlightedDocument document) => FormatText(document,
        PSStyle.Instance.OutputRendering != OutputRendering.PlainText, graphics: !Console.IsOutputRedirected);

    internal static string FormatText(HighlightedDocument document, bool ansi, bool graphics = false) {
        var writer = TextBufferWriter.Rent();
        try {
            Write(document, writer, ansi, graphics: graphics);
            return writer.ToString();
        }
        finally {
            TextBufferWriter.Return(writer);
        }
    }
}
