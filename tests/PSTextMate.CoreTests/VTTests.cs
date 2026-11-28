using System.Runtime.CompilerServices;
using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class VTTests {
    [Fact]
    public void Sixel_ExtractsBorrowedPayloadWithoutExecutingModulePlacementCommands() {
        string payload = VT.SixelStart + "\"1;1;2;6#1;2;100;0;0~~" + VT.StringTerminator;
        string wrapped = VT.SaveCursor + payload + VT.RestoreCursor;
        ReadOnlyMemory<char> extracted = VT.ExtractSixel(wrapped);
        Assert.Equal(payload, extracted.ToString());
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetString(extracted, out string? owner, out _, out _));
        Assert.Same(wrapped, owner);
        Assert.True(VT.ExtractSixel(VT.SixelStart + "\u001b[2J" + VT.StringTerminator).IsEmpty);
        Assert.True(VT.ExtractSixel("plain").IsEmpty);
    }

    [Fact]
    public void PagerModes_SaveRestoreAndEnableWheelTranslation() {
        using var writer = new TextBufferWriter();
        VT.EnterPager(writer);
        string enter = writer.ToString();
        Assert.StartsWith(VT.SavePagerModes + VT.DisableMouseReporting + VT.EnterAlternateScreen, enter);
        Assert.Contains(VT.EnableAlternateScroll, enter);
        writer.Clear();
        VT.LeavePager(writer, true);
        Assert.Equal(VT.EndSynchronizedOutput + VT.CloseHyperlink + VT.Reset + VT.ShowCursor
            + VT.LeaveAlternateScreen + VT.DisableAlternateScroll + VT.RestorePagerModes, writer.ToString());
    }

    [Fact]
    public void CursorCommands_WriteInvariantNumbersToSpanBuffer() {
        using var writer = new TextBufferWriter();
        writer.MoveTo(123, 456);
        VT.MoveUp(writer, 7);
        VT.MoveRight(writer, 8);
        VT.MoveRight(writer, 0);
        Assert.Equal("\u001b[456;123H\u001b[7A\u001b[8C", writer.ToString());
    }
}
