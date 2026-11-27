namespace PSTextMate.Core;

public static partial class TextRenderer {
    private static void WriteImageCells(HighlightedDocument document, TextWriter writer, int row, int start, int length,
        TextStyle style, int firstLine, int lastLine) {
        HighlightedLine line = document.Lines.Span[row];
        int end = start + length;
        int gutter = document.ShowLineNumbers ? LineNumberGutter.Digits(document.LineCount) + 3 : 0;
        while (start < end) {
            int next = end;
            int coveredEnd = -1;
            for (int index = 0; index < document.Images.Length; index++) {
                TextBounds bounds = document.GetImageBounds(index);
                if (bounds.Row < firstLine || bounds.Row + bounds.Height > lastLine || row < bounds.Row || row >= bounds.Row + bounds.Height)
                    continue;
                int offset = TerminalCells.Fit(document.Text.AsSpan(line.Start, line.Length), Math.Max(0, bounds.Column - gutter));
                int from = line.Start + offset;
                int to = from + TerminalCells.Fit(document.Text.AsSpan(from, line.Length - offset), bounds.Width);
                if (from <= start && to > start)
                    coveredEnd = Math.Max(coveredEnd, Math.Min(to, end));
                else if (from > start)
                    next = Math.Min(next, from);
            }
            if (coveredEnd > start) {
                writer.Write(VT.Reset);
                TextGeometry.WritePadding(writer, TerminalCells.Measure(document.Text.AsSpan(start, coveredEnd - start)));
                start = coveredEnd;
            }
            else {
                TextOutput.Write(writer, document.Text.AsSpan(start, next - start), style);
                start = next;
            }
        }
    }

    private static void WriteCompletedImages(HighlightedDocument document, TextWriter writer, int row, bool newline, int firstLine, int lastLine) {
        for (int index = 0; index < document.Images.Length; index++) {
            TextBounds bounds = document.GetImageBounds(index);
            if (bounds.Row < firstLine || bounds.Row + bounds.Height > lastLine || row != bounds.Row + bounds.Height - 1)
                continue;
            HighlightedLine line = document.Lines.Span[row];
            int endColumn = newline ? 0 : TerminalCells.Measure(document.Text.AsSpan(line.Start, line.Length));
            if (!newline && document.ShowLineNumbers)
                endColumn += LineNumberGutter.Digits(document.LineCount) + 3;
            writer.Write('\r');
            writer.Write(VT.SaveCursor);
            writer.Write(VT.SaveSixelModes);
            try {
                VT.MoveUp(writer, bounds.Height - (newline ? 0 : 1));
                VT.MoveRight(writer, bounds.Column);
                writer.Write(VT.InlineSixelModes);
                writer.Write(document.Images.Span[index].Image.Sixel.Span);
            }
            finally {
                writer.Write(VT.RestoreSixelModes);
                writer.Write(VT.RestoreCursor);
                VT.MoveRight(writer, endColumn);
                writer.Write(VT.Reset);
            }
        }
    }
}
