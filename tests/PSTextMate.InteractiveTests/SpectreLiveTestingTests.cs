using PSTextMate.Terminal;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace PSTextMate.InteractiveTests;

public sealed class SpectreLiveTestingTests {
    [Fact]
    public void LiveDisplay_Start_ReturnsScriptBlockResult() {
        var console = new TestConsole();

        var table = new Table();
        table.AddColumn("Name");
        table.AddColumn("Value");
        table.AddRow("Test", "Value");

        int result = console.Live(table)
            .AutoClear(true)
            .Start(_ => 1);

        Assert.Equal(1, result);
    }

    [Fact]
    public void LiveDisplay_CanUpdateTargetDuringExecution() {
        var console = new TestConsole();

        _ = console.Live(new Markup("start"))
            .AutoClear(true)
            .Start(ctx => {
                ctx.UpdateTarget(new Markup("end"));
                ctx.Refresh();
                return 0;
            });

        Assert.Contains("end", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pager_Show_WithTestConsoleAndQuitKey_ExitsAndRendersContent() {
        var console = new TestConsole();
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false)
        ]);

        Markup[] renderables = [
            new Markup("alpha"),
            new Markup("beta")
        ];

        var pager = new Pager(
            renderables,
            console,
            () => keys.Count > 0 ? keys.Dequeue() : null,
            suppressTerminalControlSequences: true
        );
        pager.Show();

        Assert.Contains("alpha", console.Output, StringComparison.OrdinalIgnoreCase);
    }
}
