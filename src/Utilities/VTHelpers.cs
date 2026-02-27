using PSTextMate.Core;

namespace PSTextMate.Utilities;

public static class VTHelpers {
    public static void EnterAlternateBuffer() => Console.Write("\x1b[?1049h");
    public static void ExitAlternateBuffer() => Console.Write("\x1b[?1049l");
    public static void HideCursor() => Console.Write("\x1b[?25l");
    public static void ShowCursor() => Console.Write("\x1b[?25h");
    public static void ClearScreen() => Console.Write("\x1b[2J\x1b[H");
    public static void ClearScreenAlt() => Console.Write("\x1bc");
    public static void ClearRow(int row) => Console.Write($"\x1b[{row};1H\x1b[2K");
    public static void SetCursorPosition(int row, int column) => Console.Write($"\x1b[{row};{column}H");
    public static void CursorHome() => Console.Write("\x1b[H");
    // Set the vertical scroll region from line 1 to `height` (DECSTBM)
    public static void ReserveRow(int height) => Console.Write($"\x1b[1;{height}r");
    // Reset scroll region to full height (CSI r)
    public static void ResetScrollRegion() => Console.Write("\x1b[r");

}
