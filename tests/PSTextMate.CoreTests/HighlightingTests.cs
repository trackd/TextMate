using System.Text.RegularExpressions;
using PSTextMate.Core;


using Xunit;

namespace PSTextMate.InteractiveTests;

public sealed partial class HighlightingTests {
    [GeneratedRegex("\u001b\\[[0-9;]*m")]
    private static partial Regex AnsiPattern();
    [Theory]
    [InlineData("")]
    [InlineData("\r\n\n\r")]
    [InlineData("$value = '[red]🦊[/]'\r\n\tWrite-Output $value\n\n")]
    public void Highlight_PreservesSourceAndTerminators(string source) {
        HighlightedDocument document = SyntaxHighlighter.Highlight(source);
        using var writer = new StringWriter();
        document.WriteTo(writer, ansi: false);
        Assert.Same(source, document.Source);
        Assert.Equal(source, writer.ToString());
        Assert.Equal(source, AnsiPattern().Replace(document.ToAnsi(), string.Empty));
        foreach (HighlightedLine line in document.Lines.Span) {
            int cursor = line.Start;
            foreach (StyledRange range in document.Ranges.Span.Slice(line.FirstRange, line.RangeCount)) {
                Assert.Equal(cursor, range.Start);
                Assert.True(range.Length > 0);
                cursor += range.Length;
            }
            Assert.Equal(line.Start + line.Length, cursor);
        }
    }

    [Fact]
    public void Highlight_EmptyLineRetainsHereStringState() {
        const string source = "$value = @'\n\nbody\n'@\nGet-Item .";
        HighlightedDocument document = SyntaxHighlighter.Highlight(source);
        TextStyle body = StyleAt(document, source.IndexOf("body", StringComparison.Ordinal));
        TextStyle literal = StyleAt(SyntaxHighlighter.Highlight("'body'"), 1);
        Assert.Equal(literal.Foreground, body.Foreground);
        TextStyle command = StyleAt(document, source.IndexOf("Get-Item", StringComparison.Ordinal));
        Assert.Equal(StyleAt(SyntaxHighlighter.Highlight("Get-Item ."), 0), command);
        Assert.NotEqual(body.Foreground, command.Foreground);
    }

    [Fact]
    public void Highlight_ReusedTokenizerResetsDocumentState() {
        StyledRange[] expected = SyntaxHighlighter.Highlight("Get-Item .").Ranges.ToArray();
        SyntaxHighlighter.Highlight("$text = @'\nopen string");
        Assert.Equal(expected, SyntaxHighlighter.Highlight("Get-Item .").Ranges.ToArray());
    }

    public static TheoryData<ThemeName> Themes {
        get {
            var data = new TheoryData<ThemeName>();
            foreach (ThemeName theme in Enum.GetValues<ThemeName>())
                data.Add(theme);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Highlight_AllExistingThemesResolve(ThemeName theme) {
        HighlightedDocument document = SyntaxHighlighter.Highlight("Write-Output 'sample'", theme: theme);
        Assert.Equal(theme.ToString(), document.Theme);
        Assert.NotEmpty(document.Ranges.ToArray());
        Assert.Contains(document.Ranges.ToArray(), static range => range.Style.Foreground >= 0);
        Assert.Equal(0, document.LimitExceededCount);
    }

    [Fact]
    public void Highlight_ConcurrentCallsKeepThemeAndStateIndependent() {
        const string source = "$text = @'\n\nbody\n'@\nWrite-Output $text";
        StyledRange[] dark = SyntaxHighlighter.Highlight(source, theme: ThemeName.DarkPlus).Ranges.ToArray();
        StyledRange[] light = SyntaxHighlighter.Highlight(source, theme: ThemeName.LightPlus).Ranges.ToArray();
        Assert.NotEqual(dark[0].Style, light[0].Style);
        Parallel.For(0, 32, index => {
            bool isDark = index % 2 == 0;
            HighlightedDocument actual = SyntaxHighlighter.Highlight(source, theme: isDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
            Assert.Equal(isDark ? dark : light, actual.Ranges.ToArray());
        });
    }

    [Fact]
    public void Text_LineNumbersPreserveBlankLinesAndTrailingSpaces() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("$a = 1  \r\n\r\n", lineNumbers: true);
        using var writer = new StringWriter();
        document.WriteTo(writer, ansi: false);
        Assert.Equal("1 │ $a = 1  \r\n2 │ \r\n3 │ ", writer.ToString());
    }

    [Fact]
    public void Highlight_InvalidLanguageAndCancellationAreReported() {
        Assert.Throws<ArgumentException>(() => SyntaxHighlighter.Highlight("text", "missing-language"));
        Assert.Throws<ArgumentNullException>(() => SyntaxHighlighter.Highlight(null!));
        Assert.Throws<OperationCanceledException>(() => SyntaxHighlighter.Highlight("$a = 1", cancellationToken: new(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputRenderer.Render(SyntaxHighlighter.Highlight("$a = 1"), (OutputFormat)99));
    }

    private static TextStyle StyleAt(HighlightedDocument document, int offset) {
        foreach (StyledRange range in document.Ranges.Span) {
            if (offset >= range.Start && offset < range.Start + range.Length)
                return range.Style;
        }

        throw new InvalidOperationException($"No style at {offset}.");
    }
}
