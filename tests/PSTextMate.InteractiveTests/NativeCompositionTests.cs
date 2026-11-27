using System.Reflection;
using System.Text;
using PSTextMate.Core;
using PSTextMate.XenoAtom;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Text;
using Xunit;
using Rectangle = XenoAtom.Terminal.UI.Geometry.Rectangle;
using Style = XenoAtom.Terminal.UI.Style;

namespace PSTextMate.InteractiveTests;

public sealed class NativeCompositionTests {
    [Fact]
    public void SharedPanels_ComposeAndReflowInsideBothNativeFrameworks() {
        TextContent content = TextContent.Text("Some words that must wrap inside the shared panel.", new TextStyle(TextColor.Cyan))
            .LineNumbers().Panel("Shared", "Footer");
        HighlightedDocument document = content.ToDocument(60);
        var console = new TestConsole();
        console.Profile.Width = 24;
        var spectre = (IRenderable)new Spectre.Renderer().Render(document, default);
        console.Write(spectre);
        HighlightedDocument expected = content.Layout(24);
        Assert.Equal(expected.Text, console.Output.TrimEnd('\r', '\n'));
        var visual = (TextMateVisual)new Renderer().Render(document, default);
        visual.Measure(new LayoutConstraints(0, 24, 0, 100));
        Assert.Equal(expected.Text, visual.Paragraph.Text);
        Assert.Contains("Shared", document.ToHtml());
        Assert.All(expected.Text.Split('\n'), row => Assert.Equal(24, TerminalCells.Measure(row)));
        visual.Measure(new LayoutConstraints(0, 60, 0, 100));
        Assert.Equal(document.Text, visual.Paragraph.Text);
    }

    [Fact]
    public void CapturedOutput_MapsExtendedDecorationsIntoBothNativeFrameworks() {
        HighlightedDocument document = VT.Parse("\u001b[1;2;3;4;5;7;8;9mstyled\u001b[0m");
        var spectre = (IRenderable)new Spectre.Renderer().Render(document, default);
        var console = new TestConsole();
        var options = new RenderOptions(console.Profile.Capabilities, new Size(80, 24));
        Segment segment = Assert.Single(spectre.Render(options, 80).Where(item => item.Text == "styled"));
        Assert.Equal(Decoration.Bold | Decoration.Dim | Decoration.Italic | Decoration.Underline | Decoration.SlowBlink
            | Decoration.Invert | Decoration.Conceal | Decoration.Strikethrough, segment.Style.Decoration);
        var visual = (TextMateVisual)new Renderer().Render(document, default);
        StyledRun run = Assert.Single(visual.Paragraph.Runs.ToArray());
        Assert.Equal(global::XenoAtom.Terminal.UI.Styling.TextStyle.Bold | global::XenoAtom.Terminal.UI.Styling.TextStyle.Dim
            | global::XenoAtom.Terminal.UI.Styling.TextStyle.Italic | global::XenoAtom.Terminal.UI.Styling.TextStyle.Underline
            | global::XenoAtom.Terminal.UI.Styling.TextStyle.Blink | global::XenoAtom.Terminal.UI.Styling.TextStyle.Invert
            | global::XenoAtom.Terminal.UI.Styling.TextStyle.Hidden | global::XenoAtom.Terminal.UI.Styling.TextStyle.Strikethrough, run.Style.TextStyle);
    }

    [Fact]
    public void XenoAtom_ActuallyRendersEveryLineAndWideCharacterIntoNativeCells() {
        const string source = "first\nsecond 🦊\n\nlast";
        HighlightedDocument document = SyntaxHighlighter.Highlight(source, "plaintext");
        var visual = (TextMateVisual)new Renderer().Render(document, default);
        visual.Measure(new global::XenoAtom.Terminal.UI.Geometry.Size(40, 10));
        visual.Arrange(new Rectangle(0, 0, 40, 10));
        var buffer = new CellBuffer(40, 10);
        typeof(Visual).GetMethod("RenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(visual, [buffer]);
        int[] scalars = (int[])typeof(CellBuffer).GetField("_scalars", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(buffer)!;
        var cells = (Style[])typeof(CellBuffer).GetField("_cells", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(buffer)!;
        PropertyInfo continuation = typeof(Style).GetProperty("IsContinuation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var lines = new List<string>();
        for (int row = 0; row < 4; row++) {
            var text = new StringBuilder();
            for (int column = 0; column < 40; column++) {
                int index = (row * 40) + column;
                if ((bool)continuation.GetValue(cells[index])!)
                    continue;
                text.Append(scalars[index] == 0 ? " " : char.ConvertFromUtf32(scalars[index]));
            }
            lines.Add(text.ToString().TrimEnd());
        }
        Assert.Equal(source, string.Join('\n', lines));
    }
    [Fact]
    public void Spectre_ComposesInsidePanelAndReflowsAtContainerWidth() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("# Heading\n\nWords in a paragraph that must reflow inside its parent panel.", "markdown");
        var value = (IRenderable)new Spectre.Renderer().Render(document, new RendererOptions(Width: 100));
        var console = new TestConsole();
        console.Profile.Width = 32;
        console.Write(new Panel(value).Header("Native"));
        Assert.Contains("Heading", console.Output);
        Assert.Contains("paragraph", console.Output);
        Assert.DoesNotContain("# Heading", console.Output);
        Assert.All(console.Lines, line => Assert.True(TerminalCells.Measure(line) <= 32, line));
    }

    [Fact]
    public void XenoAtom_VisualReflowsDuringNativeMeasurementAndReusesUnchangedLayout() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("# Heading\n\nWords in a paragraph that must reflow inside the native layout.", "markdown");
        TextMateVisual visual = Assert.IsType<TextMateVisual>(new Renderer().Render(document, new RendererOptions(Width: 100)));
        visual.Measure(new LayoutConstraints(0, 24, 0, 100));
        Assert.All(visual.Document.Lines.ToArray(), line => Assert.True(TerminalCells.Measure(visual.Document.Text.AsSpan(line.Start, line.Length)) <= 24));
        global::XenoAtom.Terminal.UI.Controls.Paragraph paragraph = visual.Paragraph;
        visual.Measure(new LayoutConstraints(0, 24, 0, 100));
        Assert.Same(paragraph, visual.Paragraph);
        visual.Measure(new LayoutConstraints(0, 60, 0, 100));
        Assert.NotSame(paragraph, visual.Paragraph);
        Assert.Contains("native layout", visual.Document.Text.Replace('\n', ' '));
    }

    [Fact]
    public void TextConversion_RetainsSourceLayoutForNarrowNativeContainers() {
        HighlightedDocument source = SyntaxHighlighter.Highlight("# Heading\n\nWords that should wrap at the application's available width.", "markdown");
        HighlightedDocument renderedText = DocumentPresentation.Prepare(source, new RendererOptions(Width: 100));
        var view = new TextMateView(renderedText);
        HighlightedDocument narrow = view.Layout(20);
        Assert.All(narrow.Lines.ToArray(), line => Assert.True(TerminalCells.Measure(narrow.Text.AsSpan(line.Start, line.Length)) <= 20));
        Assert.DoesNotContain("# Heading", narrow.Text);
        Assert.Same(narrow, view.Layout(20));
        Assert.Equal(renderedText.Text, view.Layout(100).Text);
    }

    [Fact]
    public void SpectreBridge_ProducesPlainTextWithoutParsingLiteralMarkup() {
        object value = new Spectre.Renderer().Render(SyntaxHighlighter.Highlight("Write-Output '[red]🦊[/]'"), default);
        string text = Utilities.SpectreRenderBridge.RenderToString(value, true, 80);
        Assert.Contains("[red]🦊[/]", text);
        Assert.DoesNotContain('\u001b', text);
    }

    [Fact]
    public void Spectre_UnchangedRenderingReusesNativeSegments() {
        HighlightedDocument document = SyntaxHighlighter.Highlight("Write-Output 42");
        var value = (IRenderable)new Spectre.Renderer().Render(document, default);
        var console = new TestConsole();
        var options = new RenderOptions(console.Profile.Capabilities, new Size(80, 24));
        IEnumerable<Segment> first = value.Render(options, 80);
        Assert.Same(first, value.Render(options, 80));
        value.Render(options, 40);
        Assert.NotSame(first, value.Render(options, 80));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void NativeContainers_NarrowAllocationsReflowWithoutDroppingSource(int width) {
        const string text = "abcdefghijklmnopqrstuvwxyz";
        HighlightedDocument document = SyntaxHighlighter.Highlight(text, "plaintext");
        var value = (IRenderable)new Spectre.Renderer().Render(document, default);
        var console = new TestConsole();
        var options = new RenderOptions(console.Profile.Capabilities, new Size(width, 40));
        string rendered = string.Concat(value.Render(options, width).Select(segment => segment.Text));
        Assert.Equal(text, rendered.Replace("\n", "", StringComparison.Ordinal));
        var visual = (TextMateVisual)new Renderer().Render(document, default);
        visual.Measure(new LayoutConstraints(0, width, 0, 100));
        Assert.Equal(text, visual.Paragraph.Text!.Replace("\n", "", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeOutput_ExposesItsCurrentSharedDocumentAfterParentResizes() {
        HighlightedDocument document = SyntaxHighlighter.Highlight(new string('x', 100), "plaintext");
        object value = OutputRenderer.Render(document, OutputFormat.Spectre);
        ITextMateOutput native = Assert.IsAssignableFrom<ITextMateOutput>(value);
        IRenderable renderable = Assert.IsAssignableFrom<IRenderable>(value);
        var console = new TestConsole();
        var options = new RenderOptions(console.Profile.Capabilities, new Size(20, 100));
        renderable.Render(options, 20);
        Assert.Equal(5, native.Document.LineCount);
        Assert.Same(native.Document, OutputRenderer.GetDocument(value));
        Assert.Same(document.Source, native.Document.Source);
    }
}
