using System.Collections.Frozen;
using Tint.Grammars;
using Tint.Themes;

namespace PSTextMate.Core;

/// <summary>Registers additional engine sources in memory. Existing documents retain their catalog.</summary>
public static class HighlightingExtensions {
    private static readonly Lock Gate = new();
    private static readonly Lazy<HighlightingSnapshot> Initial = new(static () => new([], new Dictionary<string, IThemeSource>(StringComparer.OrdinalIgnoreCase)));
    private static HighlightingSnapshot? _current;
    internal static HighlightingSnapshot Snapshot => Volatile.Read(ref _current) ?? Initial.Value;

    public static void RegisterGrammar(IGrammarSource source, bool replace = false) {
        ArgumentNullException.ThrowIfNull(source);
        lock (Gate) {
            HighlightingSnapshot current = Snapshot;
            string? id = source.Info.LanguageId;
            bool duplicate = current.Grammars.Registry.TryGet(source.ScopeName, out _)
                || (id is not null && current.Grammars.ByLanguage.ContainsKey(id));
            if (duplicate && !replace)
                throw new ArgumentException($"Grammar '{id ?? source.ScopeName}' is already registered.", nameof(source));
            if (!replace) {
                foreach (string extension in source.Info.FileExtensions) {
                    if (current.Grammars.ByExtension.ContainsKey(extension))
                        throw new ArgumentException($"Extension '{extension}' is already registered.", nameof(source));
                }
                foreach (string alias in source.Info.Aliases) {
                    if (current.Grammars.ByLanguage.ContainsKey(alias))
                        throw new ArgumentException($"Language alias '{alias}' is already registered.", nameof(source));
                }
            }
            IGrammarSource[] sources = [.. current.Sources.Where(item => item.ScopeName != source.ScopeName
                && (id is null || !string.Equals(item.Info.LanguageId, id, StringComparison.OrdinalIgnoreCase))), source];
            Volatile.Write(ref _current, new HighlightingSnapshot(sources, current.Themes));
        }
    }

    public static void RegisterTheme(ThemeName name, IThemeSource source) => RegisterTheme(name.ToString(), source, true);

    public static void RegisterTheme(string name, IThemeSource source, bool replace = false) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        lock (Gate) {
            HighlightingSnapshot current = Snapshot;
            if (!replace && current.ThemeNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Theme '{name}' is already registered.", nameof(name));
            var themes = new Dictionary<string, IThemeSource>(current.Themes, StringComparer.OrdinalIgnoreCase) { [name] = source };
            Volatile.Write(ref _current, new HighlightingSnapshot(current.Sources, themes));
        }
    }

    public static IReadOnlyList<string> ThemeNames => Array.AsReadOnly(Snapshot.ThemeNames);
}

internal sealed class HighlightingSnapshot {
    internal IGrammarSource[] Sources { get; }
    internal FrozenDictionary<string, IThemeSource> Themes { get; }
    internal GrammarCatalog Grammars { get; }
    internal string[] ThemeNames { get; }

    internal HighlightingSnapshot(IGrammarSource[] sources, IReadOnlyDictionary<string, IThemeSource> themes) {
        Sources = sources;
        Themes = themes.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        Grammars = new GrammarCatalog(sources);
        ThemeNames = [.. Enum.GetNames<ThemeName>().Concat(themes.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }
}
