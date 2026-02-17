using System.Collections.Concurrent;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace PSTextMate.Core;

/// <summary>
/// Manages caching of expensive TextMate objects for improved performance.
/// Uses thread-safe collections to handle concurrent access patterns.
/// </summary>
internal static class CacheManager {
    private static readonly ConcurrentDictionary<ThemeName, (Registry registry, Theme theme)> _themeCache = new();
    private static readonly ConcurrentDictionary<string, IGrammar?> _grammarCache = new();

    /// <summary>
    /// Gets or creates a cached theme and registry combination.
    /// Avoids expensive reconstruction of theme objects on each operation.
    /// </summary>
    /// <param name="themeName">The theme to load</param>
    /// <returns>Cached registry and theme pair</returns>
    public static (Registry registry, Theme theme) GetCachedTheme(ThemeName themeName) {
        return _themeCache.GetOrAdd(themeName, name => {
            RegistryOptions options = new(name);
            Registry registry = new(options);
            Theme theme = registry.GetTheme();
            return (registry, theme);
        });
    }

    /// <summary>
    /// Gets or creates a cached grammar for the specified language or extension.
    /// Grammars are expensive to load and parse, so caching provides significant performance benefits.
    /// </summary>
    /// <param name="registry">Registry to load grammar from</param>
    /// <param name="grammarId">Language ID or file extension</param>
    /// <param name="isExtension">True if grammarId is a file extension, false if it's a language ID</param>
    /// <returns>Cached grammar instance or null if not found</returns>
    public static IGrammar? GetCachedGrammar(Registry registry, string grammarId, bool isExtension) {
        string cacheKey = $"{grammarId}_{isExtension}";
        return _grammarCache.GetOrAdd(cacheKey, _ => {
            RegistryOptions options = new(ThemeName.Dark); // Use default for grammar loading
            return isExtension
                ? registry.LoadGrammar(options.GetScopeByExtension(grammarId))
                : registry.LoadGrammar(options.GetScopeByLanguageId(grammarId));
        });
    }

    /// <summary>
    /// Clears all cached objects. Useful for memory management or when themes/grammars change.
    /// </summary>
    public static void ClearCache() {
        _themeCache.Clear();
        _grammarCache.Clear();
    }
}
