using System.Globalization;
using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class TerminalTextTests {
    [Theory]
    [InlineData("before \u001b[?25l after", "before ␛[?25l after")]
    [InlineData("a\u0007b\u0008c\u007fd", "a␇b␈c␡d")]
    [InlineData("a\u009bb", "a�b")]
    public void SourceControls_AreVisibleInPresentationAndLowLevelWrites(string source, string expected) {
        HighlightedDocument document = SyntaxHighlighter.Highlight(source, "plaintext");
        Assert.Equal(expected, document.ToAnsi());
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        document.WriteTo(writer, ansi: false);
        Assert.Equal(expected, writer.ToString());
        Assert.Equal(source, document.Source);
        Assert.Equal(source, document.Text);
        HighlightedDocument prepared = DocumentPresentation.Prepare(document);
        Assert.Equal(expected, prepared.Text);
        Assert.Equal(source, prepared.Source);
    }
}
