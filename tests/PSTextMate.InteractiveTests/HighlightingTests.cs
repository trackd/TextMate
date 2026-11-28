using PSTextMate.Core;
using Spectre.Console.Rendering;
using XenoAtom.Terminal.UI.Controls;
using Xunit;
namespace PSTextMate.InteractiveTests;

public sealed class NativeOutputTests {
    [Fact]
    public void Spectre_PlainCaptureOmitsAllControlSequencesAndPreservesTokenBoundaries() {
        const string source = "public class Foo { }";
        HighlightedDocument document = SyntaxHighlighter.Highlight(source, "csharp");
        object output = new Spectre.Renderer().Render(document, default);
        string plain = Utilities.SpectreRenderBridge.RenderToString(output, escapeAnsi: true, width: 80);
        Assert.Equal(source, plain);
        string ansi = Utilities.SpectreRenderBridge.RenderToString(output, width: 80);
        Assert.Contains("\u001b[", ansi);
    }

    [Fact]
    public void Spectre_ProducesNativeRenderableWithoutInterpretingMarkup() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("Write-Output '[red]🦊[/]'", lineNumbers: true);
        object output = new Spectre.Renderer().Render(document, default);
        Assert.IsAssignableFrom<IRenderable>(output);
        string text = Utilities.SpectreRenderBridge.RenderToString(output, escapeAnsi: true);
        Assert.Contains("[red]🦊[/]", text);
        Assert.Contains("1 │", text);
    }

    [Fact]
    public void Spectre_RichMarkdownRetainsHeadingAndCodeContent() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("# Heading\n\n```powershell\nGet-Item .\n```", "markdown");
        object output = new Spectre.Renderer().Render(document, default);
        string text = Utilities.SpectreRenderBridge.RenderToString(output, escapeAnsi: true);
        Assert.Contains("Heading", text);
        Assert.Contains("Get-Item", text);
        Assert.DoesNotContain("```", text);
    }

    [Fact]
    public void XenoAtom_ProducesNativeParagraphWithExactStyledOffsets() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("Write-Output '🦊'\r\n$a = 2");
        HighlightedDocument presented = DocumentPresentation.Prepare(document);
        Paragraph output = Assert.IsType<XenoAtom.TextMateVisual>(new XenoAtom.Renderer().Render(document, default)).Paragraph;
        Assert.Equal(presented.Text, output.Text);
        Assert.Equal(presented.Ranges.Length, output.Runs.Length);
        for (int index = 0; index < output.Runs.Length; index++) {
            Assert.Equal(presented.Ranges.Span[index].Start, output.Runs[index].Start);
            Assert.Equal(presented.Ranges.Span[index].Length, output.Runs[index].Length);
        }
    }

    [Fact]
    public void XenoAtom_LineNumbersShiftStylesWithoutChangingSource() {
        const string source = "Write-Output 'first'\r\n$a = 2\n";
        HighlightedDocument document = SyntaxHighlighter.Highlight(source, lineNumbers: true);
        HighlightedDocument presented = DocumentPresentation.Prepare(document);
        Paragraph output = Assert.IsType<XenoAtom.TextMateVisual>(new XenoAtom.Renderer().Render(document, default)).Paragraph;
        Assert.Equal("1 │ Write-Output 'first'\n2 │ $a = 2\n3 │ ", output.Text);
        Assert.Equal(source, document.Source);
        int secondLine = presented.Lines.Span[1].FirstRange;
        Assert.Equal(presented.Ranges.Span[0].Start + 4, output.Runs[0].Start);
        Assert.Equal(presented.Ranges.Span[secondLine].Start + 8, output.Runs[secondLine].Start);
    }

}
