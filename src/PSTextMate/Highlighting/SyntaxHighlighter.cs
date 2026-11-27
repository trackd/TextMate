using Tint;
using Tint.Themes;

namespace PSTextMate.Core;

/// <summary>Managed, renderer-independent syntax highlighting.</summary>
public static partial class SyntaxHighlighter {
    private sealed class ResolvedTheme(Theme theme) {
        internal Theme Theme { get; } = theme;
        internal int[] Colors { get; } = ResolveColors(theme);
        private static int[] ResolveColors(Theme theme) {
            int[] colors = new int[theme.ColorMap.Count + 1];
            Array.Fill(colors, -1);
            for (int i = 1; i < theme.ColorMap.Count; i++) {
                string? color = theme.ColorMap.GetColor(i);
                if (color is { Length: >= 7 })
                    colors[i] = int.Parse(color.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return colors;
        }
    }

    // One cached tokenizer per grammar/theme. A concurrent caller gets an independent tokenizer.
    private sealed class Session(Tokenizer tokenizer) {
        internal Tokenizer Tokenizer { get; } = tokenizer;
        internal List<StyledRange> Ranges { get; } = [];
        internal List<HighlightedLine> Lines { get; } = [];
    }
    private sealed class TokenizerSlot { internal Session? Value; }
    private sealed class EngineState {
        internal readonly ConcurrentDictionary<string, Lazy<ResolvedTheme>> Themes = new(StringComparer.OrdinalIgnoreCase);
        internal readonly ConcurrentDictionary<(string Scope, ResolvedTheme Theme), TokenizerSlot> Tokenizers = new();
    }
    private static readonly ConditionalWeakTable<HighlightingSnapshot, EngineState> States = [];

    internal static Theme GetTheme(string name, HighlightingSnapshot? catalog = null) => GetResolvedTheme(name, catalog ?? HighlightingExtensions.Snapshot).Theme;
    private static ResolvedTheme GetResolvedTheme(string name, HighlightingSnapshot catalog) => States.GetOrCreateValue(catalog).Themes.GetOrAdd(name,
        static (key, snapshot) => new Lazy<ResolvedTheme>(() => new(LoadTheme(key, snapshot))), catalog).Value;

    private static Theme LoadTheme(string name, HighlightingSnapshot catalog) {
        if (catalog.Themes.TryGetValue(name, out IThemeSource? custom))
            return Theme.FromSource(custom);
        if (!Enum.TryParse(name, true, out ThemeName builtin) || !Enum.IsDefined(builtin))
            throw new ArgumentException($"No TextMate theme named '{name}'.", nameof(name));
        IThemeSource source = builtin switch {
            ThemeName.Abbys => TintThemes.Abyss,
            ThemeName.Dark => TintThemes.DarkVisualStudio,
            ThemeName.DarkPlus => TintThemes.DarkPlus,
            ThemeName.DimmedMonokai => TintThemes.MonokaiDimmed,
            ThemeName.KimbieDark => TintThemes.KimbieDark,
            ThemeName.Light => TintThemes.LightVisualStudio,
            ThemeName.LightPlus => TintThemes.LightPlus,
            ThemeName.Monokai => TintThemes.Monokai,
            ThemeName.QuietLight => TintThemes.QuietLight,
            ThemeName.Red => TintThemes.Red,
            ThemeName.SolarizedDark => TintThemes.SolarizedDark,
            ThemeName.SolarizedLight => TintThemes.SolarizedLight,
            ThemeName.TomorrowNightBlue => TintThemes.TomorrowNightBlue,
            ThemeName.HighContrastLight => TintThemes.LightHighContrast,
            ThemeName.HighContrastDark => TintThemes.DarkHighContrast,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        return Theme.FromSource(source);
    }

    internal static TextStyle GetDefaultStyle(string name, HighlightingSnapshot catalog) {
        ResolvedTheme resolved = GetResolvedTheme(name, catalog);
        return new(resolved.Colors[resolved.Theme.Defaults.ForegroundId],
            resolved.Colors[resolved.Theme.Defaults.BackgroundId], TextDecoration.None);
    }

    /// <summary>Highlights source without splitting it into strings or creating per-token objects.</summary>
    public static HighlightedDocument Highlight(string source, string language = "powershell",
        ThemeName theme = ThemeName.DarkPlus, bool isExtension = false, bool lineNumbers = false,
        CancellationToken cancellationToken = default) => Highlight(source, language, theme.ToString(), isExtension, lineNumbers, cancellationToken);

    public static HighlightedDocument Highlight(string source, string language, string theme,
        bool isExtension = false, bool lineNumbers = false, CancellationToken cancellationToken = default) =>
        Highlight(source, language, theme, isExtension, lineNumbers, HighlightingExtensions.Snapshot, cancellationToken);

    internal static HighlightedDocument Highlight(string source, string language, string theme,
        bool isExtension, bool lineNumbers, HighlightingSnapshot catalog, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        cancellationToken.ThrowIfCancellationRequested();
        ResolvedTheme resolved = GetResolvedTheme(theme, catalog);
        if (language.Equals("plaintext", StringComparison.OrdinalIgnoreCase) || language.Equals("text", StringComparison.OrdinalIgnoreCase))
            return PlainText(source, lineNumbers, theme, catalog);
        Language definition = catalog.Grammars.Resolve(language, isExtension);
        TokenizerSlot slot = States.GetOrCreateValue(catalog).Tokenizers.GetOrAdd((definition.ScopeName, resolved), static _ => new());
        Session session = Interlocked.Exchange(ref slot.Value, null)
            ?? new(catalog.Grammars.Registry.CreateTokenizer(definition.ScopeName, resolved.Theme));
        Tokenizer tokenizer = session.Tokenizer;
        List<StyledRange> ranges = session.Ranges;
        List<HighlightedLine> lines = session.Lines;
        StateStack state = StateStack.Initial;
        long initialLimits = tokenizer.LimitExceededCount;
        try {
            int start = 0;
            while (start <= source.Length) {
                cancellationToken.ThrowIfCancellationRequested();
                int length = source.AsSpan(start).IndexOfAny('\r', '\n');
                if (length < 0)
                    length = source.Length - start;
                int first = ranges.Count;
                ReadOnlySpan<EncodedToken> tokens = tokenizer.TokenizeLineEncoded(source.AsSpan(start, length), state, out state);
                for (int i = 0; i < tokens.Length; i++) {
                    int tokenStart = Math.Min(tokens[i].Start, length);
                    int tokenEnd = i + 1 < tokens.Length ? Math.Min(tokens[i + 1].Start, length) : length;
                    if (tokenEnd <= tokenStart)
                        continue;
                    uint metadata = tokens[i].Metadata;
                    TextStyle style = ResolveStyle(metadata, resolved);
                    if (ranges.Count > first && ranges[^1].Style == style && ranges[^1].Start + ranges[^1].Length == start + tokenStart) {
                        StyledRange previous = ranges[^1];
                        ranges[^1] = previous with { Length = previous.Length + tokenEnd - tokenStart };
                    }
                    else {
                        ranges.Add(new(start + tokenStart, tokenEnd - tokenStart, style));
                    }
                }
                lines.Add(new(start, length, first, ranges.Count - first));
                int end = start + length;
                if (end == source.Length)
                    break;
                start = end + (source[end] == '\r' && end + 1 < source.Length && source[end + 1] == '\n' ? 2 : 1);
            }
            return new(source, definition.Id, theme, lineNumbers, [.. ranges], [.. lines],
                tokenizer.LimitExceededCount - initialLimits, catalog: catalog);
        }
        finally {
            ranges.Clear();
            lines.Clear();
            // Keep a single bounded scratch session per grammar/theme; result arrays are never pooled.
            if (source.Length <= 262144 && ranges.Capacity <= 32768 && lines.Capacity <= 8192)
                Interlocked.CompareExchange(ref slot.Value, session, null);
        }
    }

    internal static HighlightedDocument PlainText(string source, bool lineNumbers = false, string theme = "DarkPlus", HighlightingSnapshot? catalog = null) {
        var lines = new List<HighlightedLine>();
        int start = 0;
        while (start <= source.Length) {
            int length = source.AsSpan(start).IndexOfAny('\r', '\n');
            if (length < 0)
                length = source.Length - start;
            lines.Add(new(start, length, 0, 0));
            int end = start + length;
            if (end == source.Length)
                break;
            start = end + (source[end] == '\r' && end + 1 < source.Length && source[end + 1] == '\n' ? 2 : 1);
        }
        return new(source, "plaintext", theme, lineNumbers, [], [.. lines], 0, catalog: catalog);
    }
}
