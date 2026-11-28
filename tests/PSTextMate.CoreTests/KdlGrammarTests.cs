using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class KdlGrammarTests {
    [Fact]
    public void KdlV1Catalog_ResolvesAliasesAndExtension() {
        Language language = GrammarCatalog.Instance.Resolve("KDL v1", false);
        Assert.Equal("kdl", language.Id);
        Assert.Equal("source.kdl", language.ScopeName);
        Assert.Equal(language, GrammarCatalog.Instance.Resolve("KDL", false));
        Assert.Equal(language, GrammarCatalog.Instance.Resolve(".KdL", true));
        Assert.False(GrammarCatalog.Instance.ByLanguage.ContainsKey("KDL v2"));
    }

    [Fact]
    public void KdlV1Highlight_PreservesSourceAndStylesLiteralsAndStrings() {
        const string source = "// comment\nnode enabled=true empty=null count=42 title=\"sample\" {\n child false\n}\n";
        HighlightedDocument document = SyntaxHighlighter.Highlight(source, ".kdl", isExtension: true);
        Assert.Equal(0, document.LimitExceededCount);
        using var writer = new StringWriter();
        document.WriteTo(writer, ansi: false);
        Assert.Equal(source, writer.ToString());
        TextStyle comment = At("// comment");
        TextStyle literal = At("sample");
        Assert.NotEqual(comment.Foreground, literal.Foreground);
        Assert.NotEqual(literal.Foreground, At("true").Foreground);
        Assert.NotEqual(comment.Foreground, At("true").Foreground);
        Assert.Equal(At("true").Foreground, At("false").Foreground);
        Assert.NotEqual(literal.Foreground, At("42").Foreground);

        TextStyle At(string value) {
            int offset = source.IndexOf(value, StringComparison.Ordinal);
            foreach (StyledRange range in document.Ranges.Span) {
                if (offset >= range.Start && offset < range.Start + range.Length)
                    return range.Style;
            }
            throw new InvalidOperationException("Missing styled range.");
        }
    }
}
