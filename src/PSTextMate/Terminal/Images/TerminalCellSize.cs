using System.Runtime.InteropServices;

namespace PSTextMate.Core;

/// <summary>
/// Reads the pixel size of a terminal cell from the operating system without writing a query to the terminal,
/// so it is safe while another thread reads keyboard input. Unix terminals report it through TIOCGWINSZ;
/// Windows has no equivalent for pseudo consoles.
/// </summary>
internal static partial class TerminalCellSize {
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowSize {
        public ushort Rows;
        public ushort Columns;
        public ushort PixelWidth;
        public ushort PixelHeight;
    }

    internal static bool TryQuery(out int cellWidth, out int cellHeight) {
        cellWidth = 0;
        cellHeight = 0;
        if (OperatingSystem.IsWindows() || Console.IsOutputRedirected)
            return false;
        nuint request = OperatingSystem.IsLinux() ? 0x5413u : 0x40087468u;
        try {
            if (Ioctl(1, request, out WindowSize size) != 0 || size.Columns == 0 || size.Rows == 0 || size.PixelWidth == 0 || size.PixelHeight == 0)
                return false;
            cellWidth = size.PixelWidth / size.Columns;
            cellHeight = size.PixelHeight / size.Rows;
            return cellWidth is > 0 and <= 128 && cellHeight is > 0 and <= 256;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) {
            return false;
        }
    }

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int Ioctl(int descriptor, nuint request, out WindowSize size);
}
