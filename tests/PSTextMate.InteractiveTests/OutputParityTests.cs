using System.Globalization;
using System.Text;
using PSTextMate.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Text;
using Xunit;
using NeutralStyle = PSTextMate.Core.TextStyle;
using XenoParagraph = XenoAtom.Terminal.UI.Controls.Paragraph;
using XenoTextStyle = XenoAtom.Terminal.UI.Styling.TextStyle;

namespace PSTextMate.InteractiveTests;

public sealed class OutputParityTests {
    private static readonly NeutralStyle Plain = new(-1, -1, TextDecoration.None);

    [Theory]
    [InlineData("code", 80, false, false, ThemeName.DarkPlus)]
    [InlineData("code", 20, false, true, ThemeName.LightPlus)]
    [InlineData("markdown", 80, false, false, ThemeName.DarkPlus)]
    [InlineData("markdown", 40, false, true, ThemeName.LightPlus)]
    [InlineData("markdown", 20, false, false, ThemeName.DarkPlus)]
    [InlineData("markdown", 12, false, false, ThemeName.DarkPlus)]
    [InlineData("markdown", 8, false, true, ThemeName.LightPlus)]
    [InlineData("markdown", 40, true, true, ThemeName.DarkPlus)]
    [InlineData("unicode", 20, false, true, ThemeName.DarkPlus)]
    [InlineData("blank", 80, false, true, ThemeName.DarkPlus)]
    public void OutputFormats_PreserveIdenticalTextStylesLinksAndGutters(string sample, int width, bool alternate, bool numbers, ThemeName theme) {
        string source = Sample(sample);
        string language = sample is "markdown" or "unicode" ? "markdown" : "powershell";
        var options = new RendererOptions(Alternate: alternate, Width: width);
        HighlightedDocument lexical = SyntaxHighlighter.Highlight(source, language, theme, lineNumbers: numbers);
        HighlightedDocument renderedText = Assert.IsType<HighlightedDocument>(OutputRenderer.Render(lexical, OutputFormat.Text, options));

        object spectre = new Spectre.Renderer().Render(lexical, options);
        XenoParagraph xeno = Assert.IsType<XenoAtom.TextMateVisual>(new XenoAtom.Renderer().Render(lexical, options)).Paragraph;
        Assert.Equal(source, renderedText.Source);
        Snapshot expected = Model(renderedText);
        Snapshot actualSpectre = Spectre((IRenderable)spectre, width);
        Snapshot actualXeno = Xeno(xeno);
        Snapshot actualAnsi = Ansi(renderedText.ToAnsi());
        Assert.Equal(expected.Text, actualAnsi.Text);
        Assert.Equal(expected.Styles, actualAnsi.Styles);
        Assert.Equal(expected.Text, actualSpectre.Text);
        Assert.Equal(expected.Text, actualXeno.Text);
        Assert.Equal(expected.Styles, actualSpectre.Styles);
        Assert.Equal(expected.Styles, actualXeno.Styles);
        if (sample == "markdown" && !alternate && width >= 20) {
            Assert.Contains("Title", expected.Text);
            Assert.Contains("powershell", expected.Text);
            Assert.Contains("Write-Output", expected.Text);
            Assert.Contains("╭", expected.Text);
            Assert.DoesNotContain("```", expected.Text);
            Assert.Contains(expected.Styles, style => style.Hyperlink == "https://example.com/link");
            if (width >= 40) {
                Assert.Equal(TextDecoration.Bold, expected.Styles[expected.Text.IndexOf("bold", StringComparison.Ordinal)].Decoration);
                Assert.Equal(TextDecoration.Italic, expected.Styles[expected.Text.IndexOf("italic", StringComparison.Ordinal)].Decoration);
                Assert.Equal(TextDecoration.Strikethrough, expected.Styles[expected.Text.IndexOf("strike", StringComparison.Ordinal)].Decoration);
                Assert.Equal(TextDecoration.None, expected.Styles[expected.Text.IndexOf(',', StringComparison.Ordinal)].Decoration);
            }
        }
        if (sample == "markdown" && alternate)
            Assert.Contains("```powershell", expected.Text);
        if (sample == "code") {
            Assert.DoesNotContain('\t', expected.Text);
            Assert.Contains("    Write-Output", expected.Text);
        }
    }

    [Fact]
    public void Presentation_WrapsLongCodeBeforeBackendConversion() {
        string source = "'" + new string('x', 160) + "'";
        HighlightedDocument lexical = SyntaxHighlighter.Highlight(source, lineNumbers: true);
        HighlightedDocument document = DocumentPresentation.Prepare(lexical, new RendererOptions(Width: 20));
        Snapshot snapshot = Model(document);
        Assert.All(snapshot.Text.Split('\n'), line => Assert.True(line.Length <= 20, line));
        Assert.Equal(source, document.Source);
        Assert.Equal(source, document.Text.Replace("\n", "", StringComparison.Ordinal));
    }

    [Fact]
    public void OutputFormats_RenderTheSameSixelModuleCells() {
        var options = new RendererOptions(Width: 40);
        HighlightedDocument lexical = SyntaxHighlighter.Highlight("![red](red.bmp)", "markdown");
        HighlightedDocument? document = Images.Compiler.Parse(lexical, "\x1b[38;2;255;0;0;48;2;0;0;255m▀\x1b[0m\n");
        Assert.NotNull(document);
        Assert.Contains("▀", document.Text);
        Assert.Contains(document.Ranges.ToArray(), range => range.Style.Foreground == 0xff0000);
        Assert.Equal("▀", document.Text);
        Assert.Equal(0x0000ff, document.Ranges.Span[0].Style.Background);
        Snapshot expected = Model(document);
        Snapshot spectre = Spectre((IRenderable)new Spectre.Renderer().Render(document, options), 40);
        Snapshot xeno = Xeno(((XenoAtom.TextMateVisual)new XenoAtom.Renderer().Render(document, options)).Paragraph);
        Assert.Equal(expected.Text, spectre.Text);
        Assert.Equal(expected.Text, xeno.Text);
        Assert.Equal(expected.Styles, spectre.Styles);
        Assert.Equal(expected.Styles, xeno.Styles);
        Assert.Equal(expected.Styles, Ansi(document.ToAnsi()).Styles);
    }

    [Theory]
    [InlineData("\x1bPbad\x1b\\")]
    [InlineData("\x1b[2J")]
    [InlineData("\x1b[38;2;256;0;0m▀")]
    [InlineData("\x1b[38;2;1;0m▀")]
    [InlineData("\x1b[38;2;-1;0;0m▀")]
    [InlineData("bad\a")]
    public void SixelModuleCells_InvalidOutputRetainsImageLink(string text) {
        HighlightedDocument document = SyntaxHighlighter.Highlight("image", "plaintext");
        Assert.Null(Images.Compiler.Parse(document, text));
    }

    private static string Sample(string name) => name switch {
        "code" => "$value = '[red]🦊[/]'  \r\n\tWrite-Output $value\r\n\r\n",
        "markdown" => "# Title\n\n**bold**, *italic*, ~~strike~~, `inline`. [link](https://example.com/link)\n\n> quoted text\n\n- [x] done\n- [ ] todo\n  - nested\n\n| Name | Value |\n|:---|---:|\n| alpha | 42 |\n\n```powershell\nWrite-Output '[red]hello[/]'\n```\n\n![missing](missing-parity-image.png)",
        "unicode" => "# Unicode\n\n漢字 🦊 👩‍💻 e\u0301 and [リンク](https://example.com/unicode)",
        "blank" => "\r\n\n\r",
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private sealed record Snapshot(string Text, NeutralStyle[] Styles);

    private static Snapshot Model(HighlightedDocument document) {
        NeutralStyle[] styles = [.. Enumerable.Repeat(Plain, document.Text.Length)];
        foreach (StyledRange range in document.Ranges.Span)
            Array.Fill(styles, range.Style, range.Start, range.Length);
        var text = new StringBuilder();
        var outputStyles = new List<NeutralStyle>();
        int digits = document.LineNumberTotal.ToString(CultureInfo.InvariantCulture).Length;
        for (int index = 0; index < document.LineCount; index++) {
            HighlightedLine line = document.Lines.Span[index];
            if (document.ShowLineNumbers) {
                int number = document.GetLineNumber(index);
                string gutter = (number == 0 ? string.Empty : number.ToString(CultureInfo.InvariantCulture)).PadLeft(digits) + " │ ";
                text.Append(gutter);
                outputStyles.AddRange(Enumerable.Repeat(Plain, gutter.Length));
            }
            int end = index + 1 < document.LineCount ? document.Lines.Span[index + 1].Start : document.Text.Length;
            for (int offset = line.Start; offset < end; offset++) {
                if (document.Text[offset] == '\r')
                    continue;
                text.Append(document.Text[offset]);
                outputStyles.Add(document.Text[offset] == '\n' ? Plain : styles[offset]);
            }
        }
        using var writer = new StringWriter();
        document.WriteTo(writer, ansi: false);
        Assert.Equal(text.ToString(), writer.ToString().Replace("\r", "", StringComparison.Ordinal));
        return new(text.ToString(), [.. outputStyles]);
    }

    private static Snapshot Spectre(IRenderable renderable, int width) {
        var console = new TestConsole();
        console.Profile.Width = width;
        console.Profile.Capabilities.Links = true;
        var options = new RenderOptions(console.Profile.Capabilities, new Size(width, 1000));
        var text = new StringBuilder();
        var styles = new List<NeutralStyle>();
        foreach (Segment segment in renderable.Render(options, width)) {
            if (segment.IsLineBreak) {
                text.Append('\n');
                styles.Add(Plain);
                continue;
            }
            Style style = segment.Style ?? Style.Plain;
            TextDecoration decoration = TextDecoration.None;
            if ((style.Decoration & Decoration.Bold) != 0)
                decoration |= TextDecoration.Bold;
            if ((style.Decoration & Decoration.Italic) != 0)
                decoration |= TextDecoration.Italic;
            if ((style.Decoration & Decoration.Underline) != 0)
                decoration |= TextDecoration.Underline;
            if ((style.Decoration & Decoration.Strikethrough) != 0)
                decoration |= TextDecoration.Strikethrough;
            var converted = new NeutralStyle(style.Foreground == Color.Default ? -1 : (style.Foreground.R << 16) | (style.Foreground.G << 8) | style.Foreground.B, style.Background == Color.Default ? -1 : (style.Background.R << 16) | (style.Background.G << 8) | style.Background.B, decoration, style.Link);
            foreach (char character in segment.Text) {
                if (character == '\r')
                    continue;
                text.Append(character);
                styles.Add(character == '\n' ? Plain : converted);
            }
        }
        return new(text.ToString(), [.. styles]);
    }

    private static Snapshot Xeno(XenoParagraph paragraph) {
        string text = paragraph.Text ?? string.Empty;
        NeutralStyle[] styles = [.. Enumerable.Repeat(Plain, text.Length)];
        foreach (StyledRun run in paragraph.Runs ?? []) {
            TextDecoration decoration = TextDecoration.None;
            if ((run.Style.TextStyle & XenoTextStyle.Bold) != 0)
                decoration |= TextDecoration.Bold;
            if ((run.Style.TextStyle & XenoTextStyle.Italic) != 0)
                decoration |= TextDecoration.Italic;
            if ((run.Style.TextStyle & XenoTextStyle.Underline) != 0)
                decoration |= TextDecoration.Underline;
            if ((run.Style.TextStyle & XenoTextStyle.Strikethrough) != 0)
                decoration |= TextDecoration.Strikethrough;
            int foreground = run.Style.TryGetForeground(out global::XenoAtom.Terminal.UI.Color fg) ? (fg.R << 16) | (fg.G << 8) | fg.B : -1;
            int background = run.Style.TryGetBackground(out global::XenoAtom.Terminal.UI.Color bg) ? (bg.R << 16) | (bg.G << 8) | bg.B : -1;
            Array.Fill(styles, new NeutralStyle(foreground, background, decoration), run.Start, run.Length);
        }
        foreach (HyperlinkRun link in paragraph.Hyperlinks ?? []) {
            for (int index = link.Start; index < link.Start + link.Length; index++)
                styles[index] = styles[index] with { Hyperlink = link.Uri };
        }

        var normalizedText = new StringBuilder();
        var normalizedStyles = new List<NeutralStyle>();
        for (int index = 0; index < text.Length; index++) {
            if (text[index] == '\r')
                continue;
            normalizedText.Append(text[index]);
            normalizedStyles.Add(text[index] == '\n' ? Plain : styles[index]);
        }
        return new(normalizedText.ToString(), [.. normalizedStyles]);
    }

    private static Snapshot Ansi(string ansi) {
        using var parser = new AnsiStyledTextParser();
        var text = new StringBuilder();
        var styles = new List<NeutralStyle>();
        foreach (AnsiStyledRun run in parser.Parse(ansi)) {
            var flags = (XenoTextStyle)(int)run.Style.Decorations;
            TextDecoration decoration = TextDecoration.None;
            if ((flags & XenoTextStyle.Bold) != 0)
                decoration |= TextDecoration.Bold;
            if ((flags & XenoTextStyle.Italic) != 0)
                decoration |= TextDecoration.Italic;
            if ((flags & XenoTextStyle.Underline) != 0)
                decoration |= TextDecoration.Underline;
            if ((flags & XenoTextStyle.Strikethrough) != 0)
                decoration |= TextDecoration.Strikethrough;
            var style = new NeutralStyle(run.Style.Foreground is { } fg && fg.Kind != AnsiColorKind.Default ? (fg.R << 16) | (fg.G << 8) | fg.B : -1, run.Style.Background is { } bg && bg.Kind != AnsiColorKind.Default ? (bg.R << 16) | (bg.G << 8) | bg.B : -1, decoration, run.Hyperlink?.Uri);
            foreach (char character in run.Text) {
                if (character == '\r')
                    continue;
                text.Append(character);
                styles.Add(character == '\n' ? Plain : style);
            }
        }
        return new(text.ToString(), [.. styles]);
    }
}
