using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Spectre.Console;
using TextMateSharp.Themes;

namespace PSTextMate.Core;

/// <summary>
/// Spectre.Console implementation of ITextMateStyler.
/// Caches Style objects to avoid repeated creation.
/// </summary>
internal class SpectreTextMateStyler : ITextMateStyler {
    /// <summary>
    /// Cache: (scopesKey, themeHash) → Style
    /// </summary>
    private readonly ConcurrentDictionary<(string scopesKey, int themeHash), Style?>
        _styleCache = new();

    public Style? GetStyleForScopes(IEnumerable<string> scopes, Theme theme) {
        if (scopes == null)
            return null;

        // Create cache key from scopes and theme instance
        string scopesKey = string.Join(",", scopes);
        int themeHash = RuntimeHelpers.GetHashCode(theme);
        (string scopesKey, int themeHash) cacheKey = (scopesKey, themeHash);

        // Return cached style or compute new one
        return _styleCache.GetOrAdd(cacheKey, _ => ComputeStyle(scopes, theme));
    }

    public Text ApplyStyle(string text, Style? style)
        => string.IsNullOrEmpty(text) ? Text.Empty : new Text(text, style ?? Style.Plain);

    /// <summary>
    /// Computes the Spectre Style for a scope hierarchy by looking up theme rules.
    /// Follows same pattern as TokenProcessor.GetStyleForScopes for consistency.
    /// </summary>
    private static Style? ComputeStyle(IEnumerable<string> scopes, Theme theme) {
        // Convert to list if not already (theme.Match expects IList<string>)
        IList<string> scopesList = scopes as IList<string> ?? [.. scopes];

        int foreground = -1;
        int background = -1;
        FontStyle fontStyle = FontStyle.NotSet;

        // Match all applicable theme rules for this scope hierarchy
        foreach (ThemeTrieElementRule? rule in theme.Match(scopesList)) {
            if (foreground == -1 && rule.foreground > 0)
                foreground = rule.foreground;
            if (background == -1 && rule.background > 0)
                background = rule.background;
            if (fontStyle == FontStyle.NotSet && rule.fontStyle > 0)
                fontStyle = rule.fontStyle;
        }

        // No matching rules found
        if (foreground == -1 && background == -1 && fontStyle == FontStyle.NotSet)
            return null;

        // Use StyleHelper for consistent color and decoration conversion
        Color? foregroundColor = StyleHelper.GetColor(foreground, theme);
        Color? backgroundColor = StyleHelper.GetColor(background, theme);
        Decoration decoration = StyleHelper.GetDecoration(fontStyle);

        return new Style(foregroundColor, backgroundColor, decoration);
    }
}
