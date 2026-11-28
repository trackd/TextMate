using System.Net;
using System.Text.RegularExpressions;
using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed partial class HtmlOutputTests {
    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
    [Theory]
    [InlineData("")]
    [InlineData("\nleading newline")]
    [InlineData("<script>alert(\"&'🦊\")</script>\r\n\n")]
    public void Html_EscapesSourceAndPreservesVisibleText(string source) {
        HighlightedDocument document = SyntaxHighlighter.Highlight(source, "plaintext");
        string html = Assert.IsType<string>(document.ConvertTo(OutputFormat.Html));
        Assert.DoesNotContain("<script>", html);
        Assert.Equal(OutputRenderer.GetDocument(html).Text, WebUtility.HtmlDecode(Tags().Replace(html, string.Empty)));
        Assert.Equal(source, OutputRenderer.GetDocument(html).Source);
    }

    [Fact]
    public void Html_PreservesStylesGutterAndTerminators() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("Write-Output '<&>'\r\n\r\n", lineNumbers: true);
        string html = document.ToHtml();
        Assert.Contains("color:#", html);
        Assert.Contains("&lt;&amp;&gt;", html);
        using var plain = new StringWriter();
        ((HighlightedDocument)document.ConvertTo(OutputFormat.Text)).WriteTo(plain, false);
        Assert.Equal(plain.ToString(), WebUtility.HtmlDecode(Tags().Replace(html, string.Empty)));
    }

    [Fact]
    public void Html_FullDocumentAndStreamingProduceIdenticalOutput() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("42", "plaintext");
        using var writer = new StringWriter();
        document.WriteHtml(writer, true);
        Assert.Equal(document.ToHtml(true), writer.ToString());
        Assert.StartsWith("<!doctype html>", writer.ToString());
        Assert.EndsWith("</body></html>", writer.ToString());
    }

    [Fact]
    public void Text_ConversionRetainsPreparedObjectAndRejectsInvalidFormat() {
        HighlightedDocument document = Assert.IsType<HighlightedDocument>(SyntaxHighlighter.Highlight("42").ConvertTo(OutputFormat.Text));
        Assert.Same(document, document.ConvertTo(OutputFormat.Text));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ConvertTo((OutputFormat)999));
        Assert.Equal((string[])["Text", "Spectre", "Fullscreen", "Html"], Enum.GetNames<OutputFormat>());
        Assert.Equal(15, Enum.GetValues<ThemeName>().Length);
    }
}
