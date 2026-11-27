namespace PSTextMate.Core;

/// <summary>Displays source control characters as glyphs, never terminal instructions.</summary>
internal static class TerminalText {
    private static readonly SearchValues<char> Controls = CreateControls();

    internal static bool HasControls(ReadOnlySpan<char> text) => text.ContainsAny(Controls);

    internal static void WriteSafe(TextWriter writer, ReadOnlySpan<char> text) {
        int index;
        while ((index = text.IndexOfAny(Controls)) >= 0) {
            writer.Write(text[..index]);
            writer.Write(Visible(text[index]));
            text = text[(index + 1)..];
        }
        writer.Write(text);
    }

    internal static char Visible(char value) => value < ' ' ? (char)('\u2400' + value)
        : value == '\u007f' ? '\u2421' : '\ufffd';

    internal static string MakeSafe(string text) => string.Create(text.Length, text, static (destination, source) => {
        for (int index = 0; index < source.Length; index++) {
            char value = source[index];
            destination[index] = char.IsControl(value) && value is not '\t' and not '\r' and not '\n' ? Visible(value) : value;
        }
    });

    private static SearchValues<char> CreateControls() {
        Span<char> characters = stackalloc char[64];
        int count = 0;
        for (char value = '\0'; value < ' '; value++) {
            if (value is not '\t' and not '\r' and not '\n')
                characters[count++] = value;
        }

        for (char value = '\u007f'; value <= '\u009f'; value++)
            characters[count++] = value;
        return SearchValues.Create(characters[..count]);
    }
}
