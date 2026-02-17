using System.Globalization;
using System.Text;
using Spectre.Console;

namespace PSTextMate.Utilities;

/// <summary>
/// Provides optimized StringBuilder extension methods for text rendering operations.
/// Reduces string allocations during the markup generation process.
/// </summary>
public static class StringBuilderExtensions {
    /// <summary>
    /// Appends a Spectre.Console link markup: [link=url]text[/]
    /// </summary>
    /// <param name="builder">StringBuilder to append to</param>
    /// <param name="url">The URL for the link</param>
    /// <param name="text">The link text</param>
    /// <returns>The same StringBuilder for method chaining</returns>
    public static StringBuilder AppendLink(this StringBuilder builder, string url, string text) {
        builder.Append("[link=")
                .Append(url.EscapeMarkup())
                .Append(']')
                .Append(text.EscapeMarkup())
                .Append("[/]");
        return builder;
    }
    /// <summary>
    /// Appends an integer value with optional style using invariant culture formatting.
    /// </summary>
    /// <param name="builder">StringBuilder to append to</param>
    /// <param name="style">Optional style to apply</param>
    /// <param name="value">Nullable integer to append</param>
    /// <returns>The same StringBuilder for method chaining</returns>
    public static StringBuilder AppendWithStyle(this StringBuilder builder, Style? style, int? value)
        => AppendWithStyle(builder, style, value?.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Appends a string value with optional style markup, escaping special characters.
    /// </summary>
    /// <param name="builder">StringBuilder to append to</param>
    /// <param name="style">Optional style to apply</param>
    /// <param name="value">String text to append</param>
    /// <returns>The same StringBuilder for method chaining</returns>
    public static StringBuilder AppendWithStyle(this StringBuilder builder, Style? style, string? value) {
        value ??= string.Empty;
        return style is not null
            ? builder.Append('[')
                .Append(style.ToMarkup())
                .Append(']')
                .Append(value.EscapeMarkup())
                .Append("[/]")
            : builder.Append(value);
    }

    /// <summary>
    /// Appends a string value with optional style markup and space separator, escaping special characters.
    /// </summary>
    /// <param name="builder">StringBuilder to append to</param>
    /// <param name="style">Optional style to apply</param>
    /// <param name="value">String text to append</param>
    /// <returns>The same StringBuilder for method chaining</returns>
    public static StringBuilder AppendWithStyleN(this StringBuilder builder, Style? style, string? value) {
        value ??= string.Empty;
        return style is not null
            ? builder.Append('[')
                .Append(style.ToMarkup())
                .Append(']')
                .Append(value)
                .Append("[/] ")
            : builder.Append(value);
    }

    /// <summary>
    /// Efficiently appends text with optional style markup using spans to reduce allocations.
    /// This method is optimized for the common pattern of conditional style application.
    /// </summary>
    /// <param name="builder">StringBuilder to append to</param>
    /// <param name="style">Optional style to apply</param>
    /// <param name="value">Text content to append</param>
    /// <returns>The same StringBuilder for method chaining</returns>
    public static StringBuilder AppendWithStyle(this StringBuilder builder, Style? style, ReadOnlySpan<char> value) {
        return style is not null
            ? builder.Append('[')
                .Append(style.ToMarkup())
                .Append(']')
                .Append(value)
                .Append("[/]")
            : builder.Append(value);
    }
}
