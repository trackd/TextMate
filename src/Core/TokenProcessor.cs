using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using PSTextMate.Utilities;
using Spectre.Console;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PSTextMate.Core;

/// <summary>
/// Provides optimized token processing and styling operations.
/// Handles theme property extraction and token rendering with performance optimizations.
/// </summary>
internal static class TokenProcessor {
    private static readonly ConcurrentDictionary<(string scopesKey, int themeHash), (int fg, int bg, FontStyle fs)> _themePropertyCache = new();
    // Cache Style results per (scopesKey, themeInstanceHash)
    private static readonly ConcurrentDictionary<(string scopesKey, int themeHash), Style?> _styleCache = new();

    /// <summary>
    /// Processes tokens in batches for better cache locality and performance.
    /// This version uses the style cache to avoid re-creating Style objects per token
    /// and appends text directly into the provided StringBuilder to avoid temporary strings.
    /// </summary>
    /// <param name="tokens">Tokens to process</param>
    /// <param name="line">Source line text</param>
    /// <param name="theme">Theme for styling</param>
    /// <param name="builder">StringBuilder for output</param>
    /// <param name="lineIndex">Line index for debugging context</param>
    /// <param name="escapeMarkup">Whether to escape markup characters (true for normal text, false for code blocks)</param>
    public static void ProcessTokensBatch(
        IToken[] tokens,
        string line,
        Theme theme,
        StringBuilder builder,
        int? lineIndex = null,
        bool escapeMarkup = true) {
        foreach (IToken token in tokens) {
            int startIndex = Math.Min(token.StartIndex, line.Length);
            int endIndex = Math.Min(token.EndIndex, line.Length);

            if (startIndex >= endIndex) continue;

            ReadOnlySpan<char> textSpan = line.SpanSubstring(startIndex, endIndex);

            // Use cached Style where possible to avoid rebuilding Style objects per token
            Style? style = GetStyleForScopes(token.Scopes, theme);

            // Only extract numeric theme properties when debugging is enabled to reduce work
            (int foreground, int background, FontStyle fontStyle) = (-1, -1, FontStyle.NotSet);

            // Use the returning API so callers can append with style consistently (prevents markup regressions)
            (string processedText, Style? resolvedStyle) = WriteTokenReturn(textSpan, style, theme, escapeMarkup);
            builder.AppendWithStyle(resolvedStyle, processedText);

        }
    }

    public static (int foreground, int background, FontStyle fontStyle) ExtractThemeProperties(IToken token, Theme theme) {
        // Build a compact key from token scopes (they're mostly immutable per token)
        string scopesKey = string.Join('\u001F', token.Scopes);
        int themeHash = RuntimeHelpers.GetHashCode(theme);
        (string scopesKey, int themeHash) cacheKey = (scopesKey, themeHash);

        if (_themePropertyCache.TryGetValue(cacheKey, out (int fg, int bg, FontStyle fs) cached)) {
            return (cached.fg, cached.bg, cached.fs);
        }

        int foreground = -1;
        int background = -1;
        FontStyle fontStyle = FontStyle.NotSet;

        foreach (ThemeTrieElementRule? themeRule in theme.Match(token.Scopes)) {
            if (foreground == -1 && themeRule.foreground > 0)
                foreground = themeRule.foreground;
            if (background == -1 && themeRule.background > 0)
                background = themeRule.background;
            if (fontStyle == FontStyle.NotSet && themeRule.fontStyle > 0)
                fontStyle = themeRule.fontStyle;
        }

        // Store in cache even if defaults (-1) for future lookups
        (int foreground, int background, FontStyle fontStyle) result = (foreground, background, fontStyle);
        _themePropertyCache.TryAdd(cacheKey, result);
        return result;
    }

    /// <summary>
    /// Returns processed text and Style for the provided token span. This is the non-allocating
    /// replacement for the original API callers previously relied on. It preserves the behavior
    /// where the caller appends via AppendWithStyle so that Markup escaping and concatenation
    /// semantics remain identical.
    /// </summary>
    public static (string processedText, Style? style) WriteTokenReturn(
        ReadOnlySpan<char> text,
        Style? styleHint,
        Theme theme,
        bool escapeMarkup = true) {
        string processedText = escapeMarkup ? Markup.Escape(text.ToString()) : text.ToString();

        // Early return for no styling needed
        if (styleHint is null) {
            return (processedText, null);
        }

        // If the style serializes to an empty markup string, treat it as no style
        // to avoid emitting empty [] tags which Spectre.Markup rejects.
        string styleMarkup = styleHint.ToMarkup();
        if (string.IsNullOrEmpty(styleMarkup)) {
            return (processedText, null);
        }

        // Otherwise return the style as resolved
        return (processedText, styleHint);
    }

    /// <summary>
    /// Append the provided text span into the builder with optional style and optional markup escaping.
    /// (Existing fast-path writer retained for specialized callers.)
    /// </summary>
    public static void WriteToken(
        StringBuilder builder,
        ReadOnlySpan<char> text,
        Style? style,
        Theme theme,
        bool escapeMarkup = true) {
        // Fast-path: if no escaping needed, append span directly with style-aware overload
        if (!escapeMarkup) {
            if (style is not null) {
                string styleMarkup = style.ToMarkup();
                if (!string.IsNullOrEmpty(styleMarkup)) {
                    builder.Append('[').Append(styleMarkup).Append(']').Append(text).Append("[/]").AppendLine();
                }
                else {
                    builder.Append(text).AppendLine();
                }
            }
            else {
                builder.Append(text).AppendLine();
            }
            return;
        }

        // Check for presence of characters that require escaping. Most common tokens do not contain '[' or ']'
        bool needsEscape = false;
        foreach (char c in text) {
            if (c is '[' or ']') {
                needsEscape = true;
                break;
            }
        }

        if (!needsEscape) {
            // Safe fast-path: append span directly
            if (style is not null) {
                string styleMarkup = style.ToMarkup();
                if (!string.IsNullOrEmpty(styleMarkup)) {
                    builder.Append('[').Append(styleMarkup).Append(']').Append(text).Append("[/]").AppendLine();
                }
                else {
                    builder.Append(text).AppendLine();
                }
            }
            else {
                builder.Append(text).AppendLine();
            }
            return;
        }

        // Slow path: fallback to the reliable Markup.Escape for correctness when special characters are present
        string escaped = Markup.Escape(text.ToString());
        if (style is not null) {
            string styleMarkup = style.ToMarkup();
            if (!string.IsNullOrEmpty(styleMarkup)) {
                builder.Append('[').Append(styleMarkup).Append(']').Append(escaped).Append("[/]").AppendLine();
            }
            else {
                builder.Append(escaped).AppendLine();
            }
        }
        else {
            builder.Append(escaped).AppendLine();
        }
    }

    /// <summary>
    /// Processes tokens and appends their text into the provided Paragraph using Spectre styles.
    /// This avoids building markup strings and lets Spectre handle rendering directly.
    /// </summary>
    public static void ProcessTokensToParagraph(
        IToken[] tokens,
        string line,
        Theme theme,
        Paragraph paragraph,
        bool escapeMarkup = true) {

        foreach (IToken token in tokens) {
            int startIndex = Math.Min(token.StartIndex, line.Length);
            int endIndex = Math.Min(token.EndIndex, line.Length);
            if (startIndex >= endIndex) continue;

            string text = line[startIndex..endIndex];
            Style? style = GetStyleForScopes(token.Scopes, theme);

            // Paragraph.Append does not interpret Spectre markup, so no escaping is necessary.
            if (style is not null) {
                paragraph.Append(text, style);
            }
            else {
                paragraph.Append(text, Style.Plain);
            }
        }
    }

    /// <summary>
    /// Returns a cached Style for the given scopes and theme. Returns null for default/no-style.
    /// </summary>
    public static Style? GetStyleForScopes(IEnumerable<string> scopes, Theme theme) {
        string scopesKey = string.Join('\u001F', scopes);
        int themeHash = RuntimeHelpers.GetHashCode(theme);
        (string scopesKey, int themeHash) cacheKey = (scopesKey, themeHash);

        if (_styleCache.TryGetValue(cacheKey, out Style? cached)) {
            return cached;
        }

        // Fallback to extracting properties and building a Style
        // Create a dummy token-like enumerable for existing ExtractThemeProperties method
        var token = new MarkdownToken([.. scopes]);
        (int fg, int bg, FontStyle fs) = ExtractThemeProperties(token, theme);
        if (fg == -1 && bg == -1 && fs == FontStyle.NotSet) {
            _styleCache.TryAdd(cacheKey, null);
            return null;
        }

        Color? foregroundColor = fg != -1 ? StyleHelper.GetColor(fg, theme) : null;
        Color? backgroundColor = bg != -1 ? StyleHelper.GetColor(bg, theme) : null;
        Decoration decoration = StyleHelper.GetDecoration(fs);

        var style = new Style(foregroundColor, backgroundColor, decoration);
        _styleCache.TryAdd(cacheKey, style);
        return style;
    }

}
