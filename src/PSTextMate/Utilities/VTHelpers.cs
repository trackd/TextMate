namespace PSTextMate.Utilities;

public static class VTHelpers {
    private static bool? _supportsAlternateBuffer;
    private static bool? _supportsSynchronizedOutput;
    private const string AlternateBufferModeQuery = "[?1049$p";
    private const string AlternateBufferReply = "[?1049;1$y";
    private const string MainBufferReply = "[?1049;2$y";
    private const string SynchronizedOutputModeQuery = "[?2026$p";
    private const string SynchronizedOutputActiveReply = "[?2026;1$y";
    private const string SynchronizedOutputInactiveReply = "[?2026;2$y";
    private const string BeginSynchronizedOutputSequence = "\x1b[?2026h";
    private const string EndSynchronizedOutputSequence = "\x1b[?2026l";
    public static void HideCursor() => Console.Write("\x1b[?25l");
    public static void ShowCursor() => Console.Write("\x1b[?25h");
    public static void ClearScreen() => Console.Write("\x1b[2J\x1b[H");
    public static void ClearScreenAlt() => ClearScreen();
    public static void ClearRow(int row) => Console.Write($"\x1b[{row};1H\x1b[2K");
    public static void SetCursorPosition(int row, int column) => Console.Write($"\x1b[{row};{column}H");
    public static void CursorHome() => Console.Write("\x1b[H");
    // Set the vertical scroll region from line 1 to `height` (DECSTBM)
    public static void ReserveRow(int height) => Console.Write($"\x1b[1;{height}r");
    // Reset scroll region to full height (CSI r)
    public static void ResetScrollRegion() => Console.Write("\x1b[r");

    /// <summary>
    /// Begins synchronized output mode (DEC private mode 2026).
    /// Unsupported terminals ignore this sequence.
    /// </summary>
    public static void BeginSynchronizedOutput() {
        if (!SupportsSynchronizedOutput()) {
            return;
        }

        Console.Write(BeginSynchronizedOutputSequence);
        Console.Out.Flush();
    }

    /// <summary>
    /// Ends synchronized output mode (DEC private mode 2026).
    /// </summary>
    public static void EndSynchronizedOutput() {
        if (!SupportsSynchronizedOutput()) {
            return;
        }

        Console.Write(EndSynchronizedOutputSequence);
        Console.Out.Flush();
    }

    /// <summary>
    /// Determines whether the terminal supports synchronized output mode 2026 using DECRQM.
    /// </summary>
    public static bool SupportsSynchronizedOutput() {
        if (_supportsSynchronizedOutput.HasValue) {
            return _supportsSynchronizedOutput.Value;
        }

        if (Console.IsOutputRedirected || Console.IsInputRedirected) {
            _supportsSynchronizedOutput = false;
            return false;
        }

        try {
            string response = Compatibility.GetControlSequenceResponse(SynchronizedOutputModeQuery);
            bool supported = response.Contains(SynchronizedOutputActiveReply, StringComparison.Ordinal)
                || response.Contains(SynchronizedOutputInactiveReply, StringComparison.Ordinal);
            _supportsSynchronizedOutput = supported;
            return supported;
        }
        catch {
            _supportsSynchronizedOutput = false;
            return false;
        }
    }

    /// <summary>
    /// Determines whether the terminal supports mode 1049 using DECRQM.
    /// </summary>
    public static bool SupportsAlternateBuffer() {
        if (_supportsAlternateBuffer.HasValue) {
            return _supportsAlternateBuffer.Value;
        }

        if (Console.IsOutputRedirected || Console.IsInputRedirected) {
            _supportsAlternateBuffer = false;
            return false;
        }

        try {
            string response = Compatibility.GetControlSequenceResponse(AlternateBufferModeQuery);
            bool supported = response.Contains(AlternateBufferReply, StringComparison.Ordinal)
                || response.Contains(MainBufferReply, StringComparison.Ordinal);
            _supportsAlternateBuffer = supported;
            return supported;
        }
        catch {
            _supportsAlternateBuffer = false;
            return false;
        }
    }

}
