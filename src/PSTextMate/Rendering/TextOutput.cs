namespace PSTextMate.Core;

/// <summary>Writes shared styles and links without constructing markup or a rendering framework object.</summary>
public static class TextOutput {
    public static void Write(TextWriter writer, ReadOnlySpan<char> text, TextStyle style, bool ansi = true) {
        ArgumentNullException.ThrowIfNull(writer);
        if (!ansi) {
            TerminalText.WriteSafe(writer, text);
            return;
        }
        VT.WriteStyle(writer, style);
        string? link = style.Hyperlink;
        if (link is not null) {
            foreach (char value in link) {
                if (char.IsControl(value)) {
                    link = null;
                    break;
                }
            }
        }
        if (!string.IsNullOrEmpty(link)) {
            writer.Write(VT.OpenHyperlink);
            writer.Write(link);
            writer.Write(VT.StringTerminator);
        }
        TerminalText.WriteSafe(writer, text);
        if (!string.IsNullOrEmpty(link))
            writer.Write(VT.CloseHyperlink);
        writer.Write(VT.Reset);
    }
}

public static class TextProgress {
    /// <summary>Computes a clamped progress fill in cells. Empty totals produce an empty bar.</summary>
    public static int Filled(int width, long completed, long total, int minimum = 0) {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        return total <= 0
            ? 0
            : Math.Clamp((int)Math.Ceiling((double)Math.Clamp(completed, 0, total) / total * width), Math.Min(minimum, width), width);
    }
}
