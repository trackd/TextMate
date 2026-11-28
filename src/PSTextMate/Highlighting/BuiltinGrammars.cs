using Tint.Grammars;
using Tint.Loading;

namespace PSTextMate.Core;

internal static class BuiltinGrammars {
    internal static IGrammarSource TomlSource { get; } = new EmbeddedGrammarSource(
        "PSTextMate.TOML.tmLanguage", "source.toml", DocumentFormat.Plist, new GrammarInfo {
            LanguageId = "toml",
            Aliases = ["TOML"],
            FileExtensions = [".toml"]
        });

    internal static IGrammarSource KdlSource { get; } = new EmbeddedGrammarSource(
        "PSTextMate.KDL.tmLanguage.json", "source.kdl", DocumentFormat.Json, new GrammarInfo {
            LanguageId = "kdl",
            Aliases = ["KDL", "KDL v1"],
            FileExtensions = [".kdl"]
        });

    private sealed class EmbeddedGrammarSource : IGrammarSource {
        public string ScopeName { get; }
        public GrammarInfo Info { get; }
        private readonly Lazy<RawGrammar> _grammar;

        internal EmbeddedGrammarSource(string resource, string scope, DocumentFormat format, GrammarInfo info) {
            ScopeName = scope;
            Info = info;
            _grammar = new(() => {
                using Stream stream = typeof(BuiltinGrammars).Assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException($"The embedded grammar '{resource}' is missing.");
                LoadedGrammar loaded = GrammarLoader.Load(stream, format, info);
                return loaded.ScopeName != scope
                    ? throw new InvalidOperationException($"The embedded grammar '{resource}' has an unexpected scope.")
                    : loaded.GetGrammar();
            });
        }

        public RawGrammar GetGrammar() => _grammar.Value;
    }
}
