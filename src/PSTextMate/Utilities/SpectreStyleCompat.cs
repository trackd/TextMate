using System.Reflection;
using Spectre.Console;

namespace PSTextMate.Utilities;

internal static class SpectreStyleCompat {
    private static readonly ConstructorInfo? LinkStyleCtor = typeof(Style).GetConstructor([typeof(Color?), typeof(Color?), typeof(Decoration?), typeof(string)]);
    private static readonly Type? LinkType = Type.GetType("Spectre.Console.Link, Spectre.Console.Ansi")
                                            ?? Type.GetType("Spectre.Console.Link, Spectre.Console");
    private static readonly ConstructorInfo? LinkCtor = LinkType?.GetConstructor([typeof(string)]);
    private static readonly MethodInfo? ParagraphAppendWithLink = FindParagraphAppendWithLink();

    public static Style Create(Color? foreground = null, Color? background = null, Decoration? decoration = null)
        => new(foreground, background, decoration);

    public static Style CreateWithLink(Color? foreground, Color? background, Decoration? decoration, string? link) {
        return string.IsNullOrWhiteSpace(link)
            ? new Style(foreground, background, decoration)
            : LinkStyleCtor is not null
            ? (Style)LinkStyleCtor.Invoke([foreground, background, decoration, link])
            : new Style(foreground, background, decoration);
    }

    public static string ToMarkup(Style? style) {
        if (style is null) {
            return string.Empty;
        }

        Style resolved = style ?? Style.Plain;
        return resolved.ToMarkup();
    }

    public static Style Resolve(Style? style) => style ?? Style.Plain;

    public static void Append(Paragraph paragraph, string text, Style? style = null, string? link = null) {
        ArgumentNullException.ThrowIfNull(paragraph);

        if (string.IsNullOrWhiteSpace(link)) {
            paragraph.Append(text, style);
            return;
        }

        if (ParagraphAppendWithLink is not null && LinkCtor is not null) {
            object? linkObject = LinkCtor.Invoke([link]);
            ParagraphAppendWithLink.Invoke(paragraph, [text, style, linkObject]);
            return;
        }

        Style baseStyle = Resolve(style);
        Style linked = CreateWithLink(baseStyle.Foreground, baseStyle.Background, baseStyle.Decoration, link);
        paragraph.Append(text, linked);
    }

    private static MethodInfo? FindParagraphAppendWithLink()
        => typeof(Paragraph)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method
                => method.Name == nameof(Paragraph.Append)
                    && method.GetParameters() is { Length: 3 } parameters
                    && parameters[2].ParameterType.Name == "Link");
}
