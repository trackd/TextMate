using System.Collections.Frozen;
using Tint;
using Tint.Grammars;

namespace PSTextMate.Core;

/// <summary>Language metadata without engine or rendering framework types.</summary>
public sealed record Language(string Id, string Name, string ScopeName,
    IReadOnlyList<string> Extensions, IReadOnlyList<string> Aliases);

internal sealed class GrammarCatalog {
    internal static GrammarCatalog Instance => HighlightingExtensions.Snapshot.Grammars;
    internal GrammarRegistry Registry { get; } = new();
    internal FrozenDictionary<string, Language> ByLanguage { get; }
    internal FrozenDictionary<string, Language> ByExtension { get; }
    internal Language[] Languages { get; }

    internal GrammarCatalog(IReadOnlyList<IGrammarSource> additional) {
        var languages = new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase);
        var extensions = new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrammarSource source in additional.Concat([BuiltinGrammars.TomlSource, BuiltinGrammars.KdlSource]).Concat(TintGrammars.All)) {
            if (!Registry.TryGet(source.ScopeName, out _))
                Registry.Add(source);
            GrammarInfo info = source.Info;
            if (info.LanguageId is not { } id)
                continue;
            string[] aliases = [.. info.Aliases];
            string[] fileExtensions = [.. info.FileExtensions];
            var language = new Language(id, aliases.Length > 0 ? aliases[0] : id, source.ScopeName,
                Array.AsReadOnly(fileExtensions), Array.AsReadOnly(aliases));
            languages.TryAdd(id, language);
            foreach (string alias in aliases)
                languages.TryAdd(alias, language);
            foreach (string extension in fileExtensions)
                extensions.TryAdd(extension, language);
        }
        languages.Add("plaintext", new("plaintext", "Plain Text", "text.plain", [], ["Plain Text"]));
        ByLanguage = languages.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        ByExtension = extensions.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        Languages = [.. languages.Values.DistinctBy(static item => item.Id).OrderBy(static item => item.Id, StringComparer.Ordinal)];
    }

    internal Language Resolve(string value, bool isExtension) {
        FrozenDictionary<string, Language> map = isExtension ? ByExtension : ByLanguage;
        return map.TryGetValue(value, out Language? language) ? language
            : throw new ArgumentException($"No TextMate grammar for {(isExtension ? "extension" : "language")} '{value}'.", nameof(value));
    }
}
