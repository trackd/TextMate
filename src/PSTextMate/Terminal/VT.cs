namespace PSTextMate.Core;

/// <summary>VT encoding and terminal commands. Policy and terminal ownership remain with the caller.</summary>
public static partial class VT {
    public const string Csi = "\u001b[";
    public const string Reset = "\u001b[0m";
    public const string ClearLine = "\u001b[2K";
    public const string ClearScreen = "\u001b[2J";
    public const string CursorHome = "\u001b[H";
    public const string HideCursor = "\u001b[?25l";
    public const string ShowCursor = "\u001b[?25h";
    public const string EnterAlternateScreen = "\u001b[?1049h";
    public const string LeaveAlternateScreen = "\u001b[?1049l";
    public const string BeginSynchronizedOutput = "\u001b[?2026h";
    public const string EndSynchronizedOutput = "\u001b[?2026l";
    public const string EnableAlternateScroll = "\u001b[?1007h";
    public const string DisableAlternateScroll = "\u001b[?1007l";
    public const string Invert = "\u001b[7m";
    public const string NormalVideo = "\u001b[27m";
    public const string OpenHyperlink = "\u001b]8;;";
    public const string StringTerminator = "\u001b\\";
    public const string CloseHyperlink = OpenHyperlink + StringTerminator;
    public const string SaveCursor = "\u001b7";
    public const string RestoreCursor = "\u001b8";
    public const string SixelStart = "\u001bP0;1q";
    public const string SavePagerModes = "\u001b[?80;8452;1000;1002;1003;1006;1007s";
    public const string DisableMouseReporting = "\u001b[?1000;1002;1003;1006l";
    public const string RestorePagerModes = "\u001b[?80;8452;1000;1002;1003;1006;1007r";
    public const string InlineSixelModes = "\u001b[?80;8452l";
    public const string SaveSixelModes = "\u001b[?80;8452s";
    public const string RestoreSixelModes = "\u001b[?80;8452r";
    /// <summary>Deletes every Kitty placement on the current screen and frees its image data. Text is unaffected.</summary>
    public const string DeleteKittyPlacements = "\u001b_Ga=d,d=A,q=2\u001b\\";

    /// <summary>
    /// Draws a retained image at the cursor. Sixel scrolling is disabled for the placement; Kitty, iTerm2 and
    /// Good Image payloads are written as they were received.
    /// </summary>
    internal static void WriteImage(TextWriter writer, TerminalImage image) {
        if (image.Protocol == TerminalImageProtocol.Sixel)
            writer.Write(InlineSixelModes);
        writer.Write(image.Payload.Span);
    }

    internal static ReadOnlyMemory<char> ExtractSixel(string text) {
        int start = text.IndexOf("\u001bP", StringComparison.Ordinal);
        if (start < 0)
            return default;
        int end = text.IndexOf(StringTerminator, start + 2, StringComparison.Ordinal);
        if (end < 0)
            return default;
        ReadOnlySpan<char> content = text.AsSpan(start + 2, end - start - 2);
        int command = content.IndexOf('q');
        if (command is < 0 or > 32)
            return default;
        foreach (char value in content[..command]) {
            if (value is not (>= '0' and <= '9') and not ';')
                return default;
        }
        foreach (char value in content[(command + 1)..]) {
            if (value is < ' ' or > '~')
                return default;
        }
        return text.AsMemory(start, end + StringTerminator.Length - start);
    }

    internal static void EnterPager(TextWriter writer) {
        writer.Write(SavePagerModes);
        writer.Write(DisableMouseReporting);
        writer.Write(EnterAlternateScreen);
        writer.Write(EnableAlternateScroll);
        writer.Write(HideCursor);
        writer.Write(Reset);
        writer.Write(ClearScreen);
    }

    internal static void LeavePager(TextWriter writer, bool cursorVisible) {
        writer.Write(EndSynchronizedOutput);
        writer.Write(CloseHyperlink);
        writer.Write(Reset);
        writer.Write(cursorVisible ? ShowCursor : HideCursor);
        writer.Write(LeaveAlternateScreen);
        writer.Write(DisableAlternateScroll);
        writer.Write(RestorePagerModes);
    }

    public static void MoveTo(TextWriter writer, int column, int row) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(row, 1);
        Span<char> buffer = stackalloc char[25];
        Csi.AsSpan().CopyTo(buffer);
        row.TryFormat(buffer[2..], out int written, provider: CultureInfo.InvariantCulture);
        int cursor = 2 + written;
        buffer[cursor++] = ';';
        column.TryFormat(buffer[cursor..], out written, provider: CultureInfo.InvariantCulture);
        cursor += written;
        buffer[cursor++] = 'H';
        writer.Write(buffer[..cursor]);
    }

    public static void MoveUp(TextWriter writer, int rows) => MoveRelative(writer, rows, 'A');
    public static void MoveRight(TextWriter writer, int columns) => MoveRelative(writer, columns, 'C');

    private static void MoveRelative(TextWriter writer, int count, char command) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
            return;
        Span<char> sequence = stackalloc char[13];
        Csi.AsSpan().CopyTo(sequence);
        count.TryFormat(sequence[2..], out int written, provider: CultureInfo.InvariantCulture);
        sequence[written + 2] = command;
        writer.Write(sequence[..(written + 3)]);
    }

    internal static void WriteRow(TextBufferWriter writer, int row, ReadOnlySpan<char> value) {
        Span<char> destination = writer.GetSpan(checked(value.Length + 27));
        Csi.AsSpan().CopyTo(destination);
        row.TryFormat(destination[2..], out int written, provider: CultureInfo.InvariantCulture);
        int cursor = written + 2;
        const string positioning = ";1H" + Reset + ClearLine;
        positioning.AsSpan().CopyTo(destination[cursor..]);
        cursor += positioning.Length;
        value.CopyTo(destination[cursor..]);
        cursor += value.Length;
        Reset.AsSpan().CopyTo(destination[cursor..]);
        writer.Advance(cursor + Reset.Length);
    }
}
