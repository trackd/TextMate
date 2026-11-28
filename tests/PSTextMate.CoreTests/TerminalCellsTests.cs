using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class TerminalCellsTests {
    [Theory]
    [InlineData("plain text", 10)]
    [InlineData("e\u0301", 1)]
    [InlineData("漢字", 4)]
    [InlineData("👨‍👩‍👧‍👦", 2)]
    [InlineData("🇸🇪", 2)]
    [InlineData("👍🏽", 2)]
    [InlineData("❤️", 2)]
    [InlineData("가", 2)]
    public void Measure_UsesTerminalGraphemeWidths(string text, int expected) => Assert.Equal(expected, TerminalCells.Measure(text));

    [Fact]
    public void Measure_HandlesUnpairedSurrogate() {
        string text = new((char)0xd800, 1);
        Assert.Equal(1, TerminalCells.Measure(text));
        Assert.Equal(1, TerminalCells.Fit(text, 1));
    }

    [Theory]
    [InlineData("e\u0301")]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData("🇸🇪")]
    [InlineData("👍🏽")]
    [InlineData("❤️")]
    [InlineData("가")]
    public void Fit_NeverSplitsAGrapheme(string cluster) {
        int width = TerminalCells.Measure(cluster);
        Assert.Equal(cluster.Length, TerminalCells.NextClusterLength(cluster + "x"));
        Assert.Equal(0, TerminalCells.Fit(cluster + "x", width - 1));
        Assert.Equal(cluster.Length, TerminalCells.Fit(cluster + "x", width));
        Assert.Equal(cluster.Length + 1, TerminalCells.Fit(cluster + "x", width + 1));
    }

    [Fact]
    public void ClusterBoundaries_PreserveRuntimeIndicSegmentation() {
        const string text = "क्‍ष";
        Assert.Equal(System.Globalization.StringInfo.GetNextTextElementLength(text),
            TerminalCells.NextClusterLength(text));
    }

    [Fact]
    public void Measurement_DoesNotAllocateAfterWarmup() {
        const string text = "abc 漢字 e\u0301 👩🏽‍💻";
        // Warm the exact measured loop so one-time runtime/tiering costs stay
        // outside the steady-state sample, including the harness itself.
        for (int index = 0; index < 32; index++)
            MeasureBatch(text);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int result = MeasureBatch(text);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(22000, result);
        Assert.Equal(0, bytes);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int MeasureBatch(string text) {
        int result = 0;
        for (int index = 0; index < 1000; index++) {
            result += TerminalCells.Measure(text);
            result += TerminalCells.Fit(text, 10);
        }
        return result;
    }
}
