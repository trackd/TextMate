namespace PSTextMate.Core;

/// <summary>Converts captured terminal output into text and styles without executing terminal commands.</summary>
public static partial class VT {
    /// <summary>
    /// Parses SGR colors/decorations, OSC 8 links and inline images (Sixel, Kitty, iTerm2 and Good Image Protocol
    /// placements). Images keep their payload and reserve their cells. Other terminal commands are discarded.
    /// </summary>
    public static HighlightedDocument Parse(string text) {
        ArgumentNullException.ThrowIfNull(text);
        var parser = new VTParser(cells: static () => TerminalImageParser.CellMetrics.Current());
        parser.Append(text);
        return parser.Build(text);
    }

    /// <param name="graphics">Cell metrics that convert image pixel sizes to cells.</param>
    public static HighlightedDocument Parse(string text, TerminalGraphics graphics) {
        ArgumentNullException.ThrowIfNull(text);
        var parser = new VTParser(cells: () => TerminalImageParser.CellMetrics.From(graphics));
        parser.Append(text);
        return parser.Build(text);
    }
}

/// <summary>Span-based VT decoder for captured output, distinct from literal source highlighting.</summary>
internal sealed class VTParser {
    private readonly PresentedTextBuilder _builder = new();
    private readonly Func<TerminalImageParser.CellMetrics>? _cells;
    private int _column;
    internal TextStyle CurrentStyle { get; private set; } = PresentedTextBuilder.Plain;
    internal VTParser(TextStyle? initialStyle = null, Func<TerminalImageParser.CellMetrics>? cells = null) {
        CurrentStyle = initialStyle ?? TextStyle.Plain;
        _cells = cells;
    }

    /// <summary>
    /// Reserves the image's cells at the cursor, like the terminal that would draw it, and leaves the cursor to the
    /// right of its last row. Returns the number of following characters that only move the cursor across the image.
    /// </summary>
    private int PlaceImage(TerminalImage image, ReadOnlySpan<char> following) {
        int column = _column;
        _builder.AddImage(_builder.Length, image);
        _builder.AppendRepeated(' ', image.Width, PresentedTextBuilder.Plain);
        for (int row = 1; row < image.Height; row++) {
            _builder.NewLine();
            _builder.AppendRepeated(' ', column + image.Width, PresentedTextBuilder.Plain);
        }
        _column = column + image.Width;
        // Some encoders follow a placement with CR LF per image row so the cursor ends below it; those rows are reserved.
        int skipped = 0;
        for (int row = 1; row < image.Height && following[skipped..].StartsWith("\r\n", StringComparison.Ordinal); row++)
            skipped += 2;
        if (skipped > 0 && following[skipped..].StartsWith("\r", StringComparison.Ordinal) && !following[skipped..].StartsWith("\r\n", StringComparison.Ordinal))
            skipped++;
        return skipped;
    }

    internal void Append(HighlightedDocument document) {
        _builder.Append(document);
        int last = document.Text.LastIndexOf('\n');
        _column = last >= 0 ? TerminalCells.Measure(document.Text.AsSpan(last + 1)) : _column + TerminalCells.Measure(document.Text);
    }

    internal void Append(ReadOnlySpan<char> text) {
        int cursor = 0;
        while (cursor < text.Length) {
            int start = cursor;
            while (cursor < text.Length && !char.IsControl(text[cursor]))
                cursor++;
            if (cursor > start) {
                ReadOnlySpan<char> run = text[start..cursor];
                _builder.Append(run, CurrentStyle);
                _column += TerminalCells.Measure(run);
            }
            if (cursor == text.Length)
                break;
            char control = text[cursor++];
            if (control == '\u001b' && _cells is not null && TerminalImageParser.MayStartImage(text[(cursor - 1)..])
                && TerminalImageParser.TryRead(text[(cursor - 1)..], _cells(), out int consumed, out TerminalImage? image)) {
                cursor += consumed - 1;
                cursor += PlaceImage(image!, text[cursor..]);
                continue;
            }
            if (control == '\t') {
                int spaces = 8 - (_column % 8);
                _builder.AppendRepeated(' ', spaces, CurrentStyle);
                _column += spaces;
            }
            else if (control is '\r' or '\n') {
                _builder.Append(text.Slice(cursor - 1, 1), CurrentStyle);
                _column = 0;
            }
            else if (control is '\u001b' or '\u009b' or '\u009d' or '\u0090' or '\u0098' or '\u009e' or '\u009f') {
                char kind = control switch {
                    '\u009b' => '[',
                    '\u009d' => ']',
                    '\u0090' => 'P',
                    '\u0098' => 'X',
                    '\u009e' => '^',
                    '\u009f' => '_',
                    _ => cursor < text.Length ? text[cursor++] : '\0'
                };
                if (kind == '[') {
                    int parameters = cursor;
                    while (cursor < text.Length && text[cursor] is >= '\x20' and <= '\x3f')
                        cursor++;
                    if (cursor < text.Length && text[cursor] is >= '\x40' and <= '\x7e') {
                        if (text[cursor] == 'm')
                            Sgr(text[parameters..cursor]);
                        cursor++;
                    }
                }
                else if (kind is ']' or 'P' or 'X' or '^' or '_') {
                    int payload = cursor;
                    while (cursor < text.Length && text[cursor] != '\u009c'
                        && !(kind == ']' && text[cursor] == '\a')
                        && !(text[cursor] == '\u001b' && cursor + 1 < text.Length && text[cursor + 1] == '\\')) {
                        cursor++;
                    }

                    if (kind == ']' && cursor < text.Length)
                        Osc(text[payload..cursor]);
                    if (cursor < text.Length)
                        cursor += text[cursor] == '\u001b' ? 2 : 1;
                }
                else {
                    // Consume the remaining bytes of an ESC intermediate/final sequence (e.g. charset selection).
                    while (kind is >= '\x20' and <= '\x2f' && cursor < text.Length)
                        kind = text[cursor++];
                }
            }
            // Other controls (bell, device replies, etc.) have no printable representation in captured output.
        }
    }

    internal HighlightedDocument Build(string source) => _builder.Build(SyntaxHighlighter.PlainText(source)).AsReflowable();

    private void Osc(ReadOnlySpan<char> payload) {
        if (!payload.StartsWith("8;", StringComparison.Ordinal))
            return;
        int separator = payload[2..].IndexOf(';');
        if (separator < 0)
            return;
        ReadOnlySpan<char> uri = payload[(separator + 3)..];
        if (uri.Length > 8192)
            return;
        foreach (char value in uri) {
            if (char.IsControl(value))
                return;
        }

        CurrentStyle = CurrentStyle with { Hyperlink = uri.IsEmpty ? null : uri.ToString() };
    }

    private void Sgr(ReadOnlySpan<char> parameters) {
        // Bound scratch space and reject overlong/invalid parameter lists without partially applying them.
        Span<int> values = stackalloc int[32];
        int count = 0;
        foreach (Range group in parameters.Split(';')) {
            ReadOnlySpan<char> field = parameters[group];
            int colon = field.IndexOf(':');
            if (colon >= 0 && int.TryParse(field[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out int command) && command is not (38 or 48)) {
                // Decoration subparameters are not independent SGR commands (4:2 is double underline, not dim).
                if (count == values.Length)
                    return;
                values[count++] = command == 4 && field[(colon + 1)..].SequenceEqual("0") ? 24 : command;
                continue;
            }
            int groupStart = count;
            foreach (Range part in field.Split(':')) {
                ReadOnlySpan<char> token = field[part];
                int number = -1;
                if (count == values.Length || (!token.IsEmpty && !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out number)))
                    return;
                values[count++] = number;
            }
            if (colon >= 0 && count - groupStart == 6 && values[groupStart + 1] == 2) {
                // ITU RGB syntax permits an empty/default color-space slot before the three components.
                if (values[groupStart + 2] > 0)
                    return;
                values.Slice(groupStart + 3, 3).CopyTo(values[(groupStart + 2)..]);
                count--;
            }
        }
        for (int index = 0; index < count; index++) {
            int code = Math.Max(0, values[index]);
            TextDecoration decoration = CurrentStyle.Decoration;
            switch (code) {
                case 0:
                    CurrentStyle = PresentedTextBuilder.Plain with { Hyperlink = CurrentStyle.Hyperlink };
                    break;
                case 1:
                    CurrentStyle = CurrentStyle with { Decoration = decoration | TextDecoration.Bold };
                    break;
                case 2:
                    CurrentStyle = CurrentStyle with { Decoration = decoration | TextDecoration.Dim };
                    break;
                case 3:
                    CurrentStyle = CurrentStyle with { Decoration = decoration | TextDecoration.Italic };
                    break;
                case 4:
                    CurrentStyle = CurrentStyle with { Decoration = decoration | TextDecoration.Underline };
                    break;
                case 5:
                    CurrentStyle = CurrentStyle with { Decoration = decoration | TextDecoration.SlowBlink };
                    break;
                case 6:
                    CurrentStyle = CurrentStyle with { Decoration = decoration | TextDecoration.RapidBlink };
                    break;
                case 7:
                    CurrentStyle = CurrentStyle with { Decoration = decoration | TextDecoration.Invert };
                    break;
                case 8:
                    CurrentStyle = CurrentStyle with { Decoration = decoration | TextDecoration.Conceal };
                    break;
                case 9:
                    CurrentStyle = CurrentStyle with { Decoration = decoration | TextDecoration.Strikethrough };
                    break;
                case 22:
                    CurrentStyle = CurrentStyle with { Decoration = decoration & ~(TextDecoration.Bold | TextDecoration.Dim) };
                    break;
                case 23:
                    CurrentStyle = CurrentStyle with { Decoration = decoration & ~TextDecoration.Italic };
                    break;
                case 24:
                    CurrentStyle = CurrentStyle with { Decoration = decoration & ~TextDecoration.Underline };
                    break;
                case 25:
                    CurrentStyle = CurrentStyle with { Decoration = decoration & ~(TextDecoration.SlowBlink | TextDecoration.RapidBlink) };
                    break;
                case 27:
                    CurrentStyle = CurrentStyle with { Decoration = decoration & ~TextDecoration.Invert };
                    break;
                case 28:
                    CurrentStyle = CurrentStyle with { Decoration = decoration & ~TextDecoration.Conceal };
                    break;
                case 29:
                    CurrentStyle = CurrentStyle with { Decoration = decoration & ~TextDecoration.Strikethrough };
                    break;
                case 39:
                    CurrentStyle = CurrentStyle with { Foreground = -1 };
                    break;
                case 49:
                    CurrentStyle = CurrentStyle with { Background = -1 };
                    break;
                case >= 30 and <= 37:
                    CurrentStyle = CurrentStyle with { Foreground = Palette(code - 30) };
                    break;
                case >= 40 and <= 47:
                    CurrentStyle = CurrentStyle with { Background = Palette(code - 40) };
                    break;
                case >= 90 and <= 97:
                    CurrentStyle = CurrentStyle with { Foreground = Palette(code - 90 + 8) };
                    break;
                case >= 100 and <= 107:
                    CurrentStyle = CurrentStyle with { Background = Palette(code - 100 + 8) };
                    break;
                case 38 or 48:
                    int color = -1;
                    if (index + 2 < count && values[index + 1] == 5) {
                        color = values[index + 2] is >= 0 and <= 255 ? Palette(values[index + 2]) : -1;
                        index += 2;
                    }
                    else if (index + 4 < count && values[index + 1] == 2) {
                        int first = index + 2;
                        if (first + 2 >= count)
                            return;
                        int r = values[first];
                        int g = values[first + 1];
                        int b = values[first + 2];
                        if (r is >= 0 and <= 255 && g is >= 0 and <= 255 && b is >= 0 and <= 255)
                            color = (r << 16) | (g << 8) | b;
                        index = first + 2;
                    }
                    else {
                        return;
                    }

                    if (color >= 0)
                        CurrentStyle = code == 38 ? CurrentStyle with { Foreground = color } : CurrentStyle with { Background = color };
                    break;
            }
        }
    }

    // Palette colors stay palette references, so captured output keeps the user's terminal theme.
    private static int Palette(int index) => ColorValue.Palette(index);
}
