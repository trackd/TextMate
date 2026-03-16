using PSTextMate.Terminal;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace PSTextMate.InteractiveTests;

public sealed class SpectreConsoleTestingTests {
    [Fact]
    public void WriterWriteToString_MatchesTestConsole_ForSimpleRenderable() {
        const int width = 48;
        var renderable = new Markup("[green]Hello[/] [bold]Pager[/]");

        var console = new TestConsole();
        console.Profile.Width = width;
        console.Write(renderable);

        string expected = console.Output.TrimEnd();
        string actual = Writer.WriteToString(renderable, width);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SpectreRenderBridge_RenderToString_StripsAnsiWhenRequested() {
        const int width = 48;
        var renderable = new Markup("[red]Error[/] [yellow]Warning[/]");

        string rendered = SpectreRenderBridge.RenderToString(renderable, escapeAnsi: false, width: width);
        string escaped = SpectreRenderBridge.RenderToString(renderable, escapeAnsi: true, width: width);

        Assert.Contains("Error", rendered);
        Assert.Contains("Warning", rendered);
        Assert.DoesNotContain(escaped, static c => c == '\u001b');
        Assert.Contains("Error", escaped);
        Assert.Contains("Warning", escaped);
    }
}
