using Tint;

namespace PSTextMate.Core;

public static partial class SyntaxHighlighter {
    /// <summary>Streams lexical highlighting without creating a document or ANSI string. Preserves whitespace; does not reflow or compile Markdown.</summary>
    public static void WriteTo(string source, TextWriter writer, string language = "powershell", string theme = "DarkPlus",
        bool lineNumbers = false, bool ansi = true, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(source);
        WriteTo(source.AsSpan(), writer, language, theme, lineNumbers, ansi, cancellationToken);
    }

    public static void WriteTo(ReadOnlySpan<char> source, TextWriter writer, string language = "powershell", string theme = "DarkPlus",
        bool lineNumbers = false, bool ansi = true, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        cancellationToken.ThrowIfCancellationRequested();
        if (ReferenceEquals(writer, Console.Out)) {
            var buffer = TextBufferWriter.Rent();
            try {
                buffer.Attach(writer);
                WriteTo(source, buffer, language, theme, lineNumbers, ansi, cancellationToken);
                buffer.Flush();
            }
            finally {
                TextBufferWriter.Return(buffer);
            }
            return;
        }
        HighlightingSnapshot catalog = HighlightingExtensions.Snapshot;
        ResolvedTheme resolved = GetResolvedTheme(theme, catalog);
        int total = lineNumbers ? CountSourceLines(source) : 1;
        bool plain = language.Equals("plaintext", StringComparison.OrdinalIgnoreCase) || language.Equals("text", StringComparison.OrdinalIgnoreCase);
        TokenizerSlot? slot = null;
        Session? session = null;
        if (!plain) {
            Language definition = catalog.Grammars.Resolve(language, isExtension: false);
            slot = States.GetOrCreateValue(catalog).Tokenizers.GetOrAdd((definition.ScopeName, resolved), static _ => new());
            session = Interlocked.Exchange(ref slot.Value, null) ?? new(catalog.Grammars.Registry.CreateTokenizer(definition.ScopeName, resolved.Theme));
        }
        try {
            StateStack state = StateStack.Initial;
            int start = 0;
            int number = 1;
            while (start <= source.Length) {
                cancellationToken.ThrowIfCancellationRequested();
                int length = source[start..].IndexOfAny('\r', '\n');
                if (length < 0)
                    length = source.Length - start;
                ReadOnlySpan<char> line = source.Slice(start, length);
                if (lineNumbers)
                    LineNumberGutter.Write(writer, number++, total);
                if (session is null) {
                    TerminalText.WriteSafe(writer, line);
                }
                else {
                    ReadOnlySpan<EncodedToken> tokens = session.Tokenizer.TokenizeLineEncoded(line, state, out state);
                    int cursor = 0;
                    int runStart = 0;
                    TextStyle runStyle = TextStyle.Plain;
                    bool hasRun = false;
                    for (int index = 0; index < tokens.Length; index++) {
                        int tokenStart = Math.Min(tokens[index].Start, length);
                        int tokenEnd = index + 1 < tokens.Length ? Math.Min(tokens[index + 1].Start, length) : length;
                        if (tokenEnd <= tokenStart)
                            continue;
                        TextStyle style = ResolveStyle(tokens[index].Metadata, resolved);
                        if (hasRun && (style != runStyle || cursor != tokenStart)) {
                            TextOutput.Write(writer, line[runStart..cursor], runStyle, ansi);
                            hasRun = false;
                        }
                        if (!hasRun) {
                            if (tokenStart > cursor)
                                TerminalText.WriteSafe(writer, line[cursor..tokenStart]);
                            runStart = tokenStart;
                            runStyle = style;
                            hasRun = true;
                        }
                        cursor = tokenEnd;
                    }
                    if (hasRun)
                        TextOutput.Write(writer, line[runStart..cursor], runStyle, ansi);
                    if (cursor < length)
                        TerminalText.WriteSafe(writer, line[cursor..]);
                }
                int end = start + length;
                if (end == source.Length)
                    break;
                int newline = source[end] == '\r' && end + 1 < source.Length && source[end + 1] == '\n' ? 2 : 1;
                writer.Write(source.Slice(end, newline));
                start = end + newline;
            }
        }
        finally {
            if (session is not null && source.Length <= 262144)
                Interlocked.CompareExchange(ref slot!.Value, session, null);
        }
    }

    private static int CountSourceLines(ReadOnlySpan<char> source) {
        int lines = 1;
        for (int index = 0; index < source.Length; index++) {
            if (source[index] == '\n') {
                lines++;
            }
            else if (source[index] == '\r') {
                lines++;
                if (index + 1 < source.Length && source[index + 1] == '\n')
                    index++;
            }
        }
        return lines;
    }

    private static TextStyle ResolveStyle(uint metadata, ResolvedTheme resolved) {
        int foreground = EncodedTokenAttributes.GetForeground(metadata);
        int background = EncodedTokenAttributes.GetBackground(metadata);
        return new(foreground == resolved.Theme.Defaults.ForegroundId ? -1 : resolved.Colors[foreground],
            background == resolved.Theme.Defaults.BackgroundId ? -1 : resolved.Colors[background],
            (TextDecoration)EncodedTokenAttributes.GetFontStyle(metadata));
    }
}
