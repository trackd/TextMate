namespace PSTextMate.Sixel;

/// <summary>
/// Provides methods and cached properties for detecting terminal compatibility, supported protocols, and cell/window sizes.
/// </summary>
public static partial class Compatibility {
    private static readonly object s_controlSequenceLock = new();
    /// <summary>
    /// Memory-caches the result of the terminal supporting sixel graphics.
    /// </summary>
    internal static bool? _terminalSupportsSixel;
    /// <summary>
    /// Memory-caches the result of the terminal cell size.
    /// </summary>
    private static CellSize? _cellSize;

    private static int? _lastWindowWidth;
    private static int? _lastWindowHeight;
    private static readonly Version MinVSCodeSixelVersion = new(1, 102, 0);
    private static readonly DateTime MinWezTermSixelBuildDate = new(2025, 3, 20);
    private static bool? _environmentSupportsSixel;

    /// <summary>
    /// Get the response to a control sequence.
    /// Only queries when it's safe to do so (no pending input, not redirected).
    /// Retries up to 2 times with 500ms timeout each.
    /// </summary>
    internal static string GetControlSequenceResponse(string controlSequence) {
        if (Console.IsOutputRedirected || Console.IsInputRedirected) {
            return string.Empty;
        }

        const int timeoutMs = 500;
        const int maxRetries = 2;

        lock (s_controlSequenceLock) {
            // Drain any stale bytes that may have leaked from prior VT interactions.
            DrainPendingInput();

            for (int retry = 0; retry < maxRetries; retry++) {
                try {
                    var response = new StringBuilder();
                    bool capturing = false;

                    // Send the control sequence
                    Console.Write($"\e{controlSequence}");
                    Console.Out.Flush();
                    var stopwatch = Stopwatch.StartNew();

                    while (stopwatch.ElapsedMilliseconds < timeoutMs) {
                        if (!TryReadAvailableKey(out char key)) {
                            Thread.Sleep(1);
                            continue;
                        }

                        if (!capturing) {
                            if (key != '\x1b') {
                                continue;
                            }
                            capturing = true;
                        }

                        response.Append(key);

                        // Check if we have a complete response
                        if (IsCompleteResponse(response)) {
                            DrainPendingInput();
                            return response.ToString();
                        }
                    }

                    // If we got a partial response, return it
                    if (response.Length > 0) {
                        DrainPendingInput();
                        return response.ToString();
                    }
                }
                catch (Exception) {
                    if (retry == maxRetries - 1) {
                        DrainPendingInput();
                        return string.Empty;
                    }
                }
            }

            DrainPendingInput();
        }

        return string.Empty;
    }

    /// <summary>
    /// Attempts to read a key if one is available.
    /// </summary>
    /// <param name="key">The key read from stdin.</param>
    /// <returns>True when a key was read, otherwise false.</returns>
    private static bool TryReadAvailableKey(out char key) {
        key = default;

        try {
            if (!Console.KeyAvailable) {
                return false;
            }

            key = Console.ReadKey(true).KeyChar;
            return true;
        }
        catch {
            return false;
        }
    }

    /// <summary>
    /// Drains any pending stdin bytes to prevent VT probe responses from leaking into user input.
    /// </summary>
    private static void DrainPendingInput() {
        if (Console.IsOutputRedirected || Console.IsInputRedirected) {
            return;
        }

        try {
            const int quietPeriodMs = 20;
            const int maxDrainMs = 250;

            var stopwatch = Stopwatch.StartNew();
            long lastReadAt = stopwatch.ElapsedMilliseconds;

            while (stopwatch.ElapsedMilliseconds < maxDrainMs) {
                if (!Console.KeyAvailable) {
                    if (stopwatch.ElapsedMilliseconds - lastReadAt >= quietPeriodMs) {
                        break;
                    }

                    Thread.Sleep(1);
                    continue;
                }

                _ = Console.ReadKey(true);
                lastReadAt = stopwatch.ElapsedMilliseconds;
            }
        }
        catch {
            // Best effort only.
        }
    }


    /// <summary>
    /// Check for complete terminal responses
    /// </summary>
    private static bool IsCompleteResponse(StringBuilder response) {
        int length = response.Length;
        if (length < 2) return false;


        // Most VT terminal responses end with specific letters
        switch (response[length - 1]) {
            // Device Attributes (ESC[...c)
            case 'c':
            // Cursor Position Report (ESC[row;columnR)
            case 'R':
            // Window manipulation (ESC[...t)
            case 't':
            // Device Status Report (ESC[...n)
            case 'n':
            // DECRPM response (ESC[?...y)
            case 'y':
                // Make sure it's actually a CSI sequence (ESC[)
                return length >= 3 && response[0] == '\x1b' && response[1] == '[';
            // String Terminator (ESC\)
            case '\\':
                return length >= 2 && response[length - 2] == '\x1b';
            // BEL character
            case (char)7:
                return true;

            default:
                // Check for Kitty graphics protocol: ends with ";OK" followed by ST and then another response
                // Minimum for ";OK" + ESC\ + ESC[...c
                if (length >= 7) {
                    // Look for ";OK" pattern
                    bool hasOK = false;
                    for (int i = 0; i <= length - 3; i++) {
                        if (response[i] == ';' && i + 2 < length &&
                            response[i + 1] == 'O' && response[i + 2] == 'K') {
                            hasOK = true;
                            break;
                        }
                    }

                    if (hasOK) {
                        // Look for ESC\ (String Terminator)
                        int stIndex = -1;
                        for (int i = 0; i < length - 1; i++) {
                            if (response[i] == '\x1b' && response[i + 1] == '\\') {
                                stIndex = i;
                                break;
                            }
                        }

                        if (stIndex >= 0 && stIndex + 2 < length) {
                            // Check if there's a complete response after the ST
                            int afterSTStart = stIndex + 2;
                            int afterSTLength = length - afterSTStart;
                            if (afterSTLength >= 3 &&
                                response[afterSTStart] == '\x1b' &&
                                response[afterSTStart + 1] == '[') {
                                char afterSTLast = response[length - 1];
                                return afterSTLast is 'c' or
                                        'R' or
                                        't' or
                                        'n' or
                                        'y';
                            }
                        }
                    }
                }
                return false;
        }
    }

    /// <summary>
    /// Get the cell size of the terminal in pixel-sixel size.
    /// The response to the command will look like [6;20;10t where the 20 is height and 10 is width.
    /// I think the 6 is the terminal class, which is not used here.
    /// </summary>
    /// <returns>The number of pixel sixels that will fit in a single character cell.</returns>
    internal static CellSize GetCellSize() {
        if (_cellSize is not null && !HasWindowSizeChanged()) {
            return _cellSize;
        }

        _cellSize = null;
        string response = GetControlSequenceResponse("[16t");

        try {
            string[] parts = response.Split(';', 't');
            if (parts.Length >= 3) {
                int width = int.Parse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture);
                int height = int.Parse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture);

                // Validate the parsed values are reasonable
                if (IsValidCellSize(width, height)) {
                    _cellSize = new CellSize {
                        PixelWidth = width,
                        PixelHeight = height
                    };
                    UpdateWindowSizeSnapshot();
                    return _cellSize;
                }
            }
        }
        catch {
            // Fall through to platform-specific fallback
        }

        // Platform-specific fallback values
        _cellSize = GetPlatformDefaultCellSize();
        UpdateWindowSizeSnapshot();
        return _cellSize;
    }

    /// <summary>
    /// Minimal validation: only ensures positive integer values.
    /// Terminal-reported cell sizes are treated as ground truth.
    /// </summary>
    private static bool IsValidCellSize(int width, int height)
        => width > 0 && height > 0;


    /// <summary>
    /// Returns platform-specific default cell size as fallback.
    /// </summary>
    private static CellSize GetPlatformDefaultCellSize() {
        // Common terminal default sizes by platform
        // macOS terminals (especially with Retina) often use 10x20
        // Windows Terminal: 10x20
        // Linux varies: 8x16 to 10x20

        return new CellSize {
            PixelWidth = 10,
            PixelHeight = 20
        };
    }

    private static bool HasWindowSizeChanged() {
        if (Console.IsOutputRedirected || Console.IsInputRedirected) {
            return false;
        }

        try {
            int currentWidth = Console.WindowWidth;
            int currentHeight = Console.WindowHeight;

            return _lastWindowWidth.HasValue &&
                _lastWindowHeight.HasValue &&
                (_lastWindowWidth.Value != currentWidth || _lastWindowHeight.Value != currentHeight);
        }
        catch {
            return false;
        }
    }

    private static void UpdateWindowSizeSnapshot() {
        if (Console.IsOutputRedirected || Console.IsInputRedirected) {
            return;
        }

        try {
            _lastWindowWidth = Console.WindowWidth;
            _lastWindowHeight = Console.WindowHeight;
        }
        catch {
            _lastWindowWidth = null;
            _lastWindowHeight = null;
        }
    }
    /// <summary>
    /// Gets the terminal height in cells. Returns 0 if the height cannot be determined.
    /// </summary>
    /// <returns>The terminal height in cells, or 0 if unavailable.</returns>
    internal static int GetTerminalHeight() {
        try {
            if (!Console.IsOutputRedirected) {
                return Console.WindowHeight;
            }
        }
        catch {
            // Terminal height is unavailable (e.g. no console attached).
        }
        return 0;
    }

    /// <summary>
    /// Check if the terminal supports sixel graphics.
    /// This is done by sending the terminal a Device Attributes request.
    /// If the terminal responds with a response that contains ";4;" then it supports sixel graphics.
    /// https://vt100.net/docs/vt510-rm/DA1.html
    /// </summary>
    /// <returns>True if the terminal supports sixel graphics, false otherwise.</returns>
    public static bool TerminalSupportsSixel() {
        if (_terminalSupportsSixel.HasValue) {
            return _terminalSupportsSixel.Value;
        }

        string response = GetControlSequenceResponse("[c");
        bool supportsSixelByDa1 = response.Contains(";4;", StringComparison.Ordinal)
            || response.Contains(";4c", StringComparison.Ordinal);

        _terminalSupportsSixel = supportsSixelByDa1 || DetectSixelSupportFromEnvironment();
        return _terminalSupportsSixel.Value;
    }

    [GeneratedRegex(@"^data:image/\w+;base64,", RegexOptions.IgnoreCase, 1000)]
    internal static partial Regex Base64Image();

    internal static string TrimBase64(string b64)
        => Base64Image().Replace(b64, string.Empty);

    /// This fallback is used only when DA1 probing does not positively identify sixel support.
    /// </summary>
    /// <returns>True when environment metadata indicates sixel support.</returns>
    private static bool DetectSixelSupportFromEnvironment() {
        if (_environmentSupportsSixel.HasValue) {
            return _environmentSupportsSixel.Value;
        }

        IDictionary env = Environment.GetEnvironmentVariables();
        bool supportsSixel = false;

        if (env["TERM_PROGRAM"] is string termProgram
            && env["TERM_PROGRAM_VERSION"] is string termProgramVersion) {
            if (termProgram.Equals("vscode", StringComparison.OrdinalIgnoreCase)) {
                supportsSixel = IsVSCodeVersionAtLeast(termProgramVersion, MinVSCodeSixelVersion);
            }
            else if (termProgram.Equals("wezterm", StringComparison.OrdinalIgnoreCase)) {
                supportsSixel = IsWezTermBuildDateAtLeast(termProgramVersion, MinWezTermSixelBuildDate);
            }
        }

        _environmentSupportsSixel = supportsSixel;
        return supportsSixel;
    }

    private static bool IsVSCodeVersionAtLeast(string termProgramVersion, Version minimumVersion) {
        int dashIdx = termProgramVersion.IndexOf('-', StringComparison.Ordinal);
        string versionPart = dashIdx > 0 ? termProgramVersion[..dashIdx] : termProgramVersion;

        return Version.TryParse(versionPart, out Version? parsedVersion)
            && parsedVersion >= minimumVersion;
    }

    private static bool IsWezTermBuildDateAtLeast(string termProgramVersion, DateTime minimumBuildDate) {
        string[] parts = termProgramVersion.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0
            && DateTime.TryParseExact(
                parts[0],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime buildDate)
            && buildDate >= minimumBuildDate;
    }
}
