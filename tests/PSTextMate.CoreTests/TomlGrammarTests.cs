using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class TomlGrammarTests {
    [Fact]
    public void TomlCatalog_ResolvesAliasAndExtension() {
        Language alias = GrammarCatalog.Instance.Resolve("TOML", false);
        Assert.Equal("toml", alias.Id);
        Assert.Equal("source.toml", alias.ScopeName);
        Assert.Equal(alias, GrammarCatalog.Instance.Resolve(".ToMl", true));
        Assert.Equal("yaml", GrammarCatalog.Instance.Resolve(".yml", true).Id);
        Assert.Equal("yaml", GrammarCatalog.Instance.Resolve(".yaml", true).Id);
    }

    [Fact]
    public void TomlHighlight_PreservesSourceAndStylesValuesAndMultilineStrings() {
        const string source = "# comment\n[server]\nport = 8080\nname = \"demo\"\nenabled = true\ntext = \"\"\"first\nsecond\n\"\"\"\n";
        HighlightedDocument document = SyntaxHighlighter.Highlight(source, ".toml", isExtension: true);
        Assert.Equal(source, document.Source);
        Assert.Equal(0, document.LimitExceededCount);
        using var writer = new StringWriter();
        document.WriteTo(writer, ansi: false);
        Assert.Equal(source, writer.ToString());
        TextStyle comment = At("# comment");
        TextStyle number = At("8080");
        TextStyle literal = At("demo");
        Assert.NotEqual(comment.Foreground, number.Foreground);
        Assert.NotEqual(number.Foreground, literal.Foreground);
        Assert.Equal(literal.Foreground, At("second").Foreground);
        Assert.NotEqual(comment.Foreground, At("true").Foreground);
        Assert.NotEqual(literal.Foreground, At("true").Foreground);

        TextStyle At(string value) {
            int offset = source.IndexOf(value, StringComparison.Ordinal);
            return document.Ranges.Span.ToArray().Single(range => offset >= range.Start && offset < range.Start + range.Length).Style;
        }
    }
}
