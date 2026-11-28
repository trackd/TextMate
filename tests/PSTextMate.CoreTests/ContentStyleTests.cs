using System.Text;
using PSTextMate.Core;
using Tint.Loading;
using Tint.Themes;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class ContentStyleTests {
    private static HighlightedDocument Document() {
        const string json = /*lang=json,strict*/ """
            {"colors":{"editor.foreground":"#112233","editor.background":"#0A0B0C"},"tokenColors":[
              {"scope":"markup.heading","settings":{"foreground":"#AA0000","fontStyle":"bold"}},
              {"scope":"markup.inline.raw.markdown","settings":{"foreground":"#00AA00"}},
              {"scope":"markup.bold.markdown","settings":{"foreground":"#0000AA","fontStyle":"bold"}}
            ]}
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        LoadedTheme theme = ThemeLoader.Load(stream, DocumentFormat.Json);
        var snapshot = new HighlightingSnapshot([], new Dictionary<string, IThemeSource> { ["Custom"] = theme });
        return SyntaxHighlighter.Highlight("source", "plaintext", "Custom", false, false, snapshot);
    }

    [Fact]
    public void SemanticRoles_ResolveOriginalScopeHierarchyAndThemeDecorations() {
        var resolver = new ContentStyleResolver(Document());
        Assert.Equal(0xaa0000, resolver.Resolve(ContentRole.Heading1).Foreground);
        Assert.Equal(0xaa0000, resolver.Resolve(ContentRole.Heading6).Foreground);
        Assert.Equal(TextDecoration.Bold, resolver.Resolve(ContentRole.Heading1).Decoration);
        Assert.Equal(0x00aa00, resolver.Resolve(ContentRole.InlineCode).Foreground);
        Assert.Equal(0x0000aa, resolver.Resolve(ContentRole.Bold).Foreground);
        Assert.Equal(-1, resolver.Resolve(ContentRole.Paragraph).Foreground);
        Assert.Equal(-1, resolver.Resolve(ContentRole.Paragraph).Background);
    }

    [Fact]
    public void SemanticRoles_CacheBaseStylesWithoutLeakingLinksOrAdditionalDecorations() {
        var resolver = new ContentStyleResolver(Document());
        TextStyle linked = resolver.Resolve(ContentRole.InlineCode, TextDecoration.Underline, "https://example.com");
        Assert.Equal("https://example.com", linked.Hyperlink);
        Assert.Equal(TextDecoration.Underline, linked.Decoration);
        TextStyle plain = resolver.Resolve(ContentRole.InlineCode);
        Assert.Null(plain.Hyperlink);
        Assert.Equal(TextDecoration.None, plain.Decoration);
        Assert.Null(resolver.Resolve(ContentRole.Link, link: "unsafe\u001b[2J").Hyperlink);
    }
}
