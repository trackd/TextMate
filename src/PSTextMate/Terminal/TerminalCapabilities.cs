using System.Management.Automation.Runspaces;

namespace PSTextMate.Core;

/// <summary>
/// The terminal's image protocols and cell size, detected like the Sixel module does: from the environment,
/// refined by one terminal probe on the first eligible TextMate command and cached for the session. Reading
/// <see cref="Current"/> never writes to or reads from the terminal.
/// </summary>
public sealed record TerminalCapabilities(string Terminal, bool Sixel, bool Kitty, bool InlineImage, bool GoodImage,
    int CellWidth, int CellHeight, bool Probed) {
    private static readonly Lock Gate = new();
    private static TerminalCapabilities? _probed;
    private static (int Columns, int Rows) _probedWindow;
    private static bool _attempted;
    private static bool _overridden;

    /// <summary>True when the cell size came from the terminal rather than the 10x20 default.</summary>
    public bool CellSizeMeasured { get; init; }

    /// <summary>The probed capabilities, or the environment's when no probe has answered.</summary>
    public static TerminalCapabilities Current => Volatile.Read(ref _probed) ?? FromEnvironment(Environment.GetEnvironmentVariable);

    /// <summary>Capabilities as the renderers use them. Good Image is a preview protocol and never selected automatically.</summary>
    public TerminalGraphics Graphics => new(Sixel, CellWidth, CellHeight, Kitty, InlineImage);

    /// <summary>
    /// Probes once per session, and again after the window was resized, when the command owns console input:
    /// the console host, not inside the prompt, PSReadLine or tab completion, with no redirection and no pending keys.
    /// </summary>
    internal static void Prepare(PSCmdlet cmdlet) {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
            return;
        (int Columns, int Rows) window;
        try {
            window = (Console.WindowWidth, Console.WindowHeight);
        }
        catch (IOException) {
            return;
        }
        lock (Gate) {
            if (_overridden || (_attempted && (_probed is null || window == _probedWindow)))
                return;
        }
        if (!CanProbe(cmdlet))
            return;
        Probe(new ConsoleQueryTransport(), window);
    }

    internal static void Probe(ITerminalQueryTransport transport, (int Columns, int Rows) window) {
        lock (Gate) {
            _attempted = true;
            string response = TerminalQuery.Read(transport);
            // A timeout is not evidence of missing support: keep what is known.
            if (response.Length == 0)
                return;
            TerminalQuery.Result result = TerminalQuery.Parse(response);
            Volatile.Write(ref _probed, FromProbe(result, Environment.GetEnvironmentVariable));
            _probedWindow = window;
        }
    }

    /// <summary>
    /// Replaces detection for the session, for example behind a multiplexer that hides the terminal's identity.
    /// No probe runs while it is set; <see cref="Reset"/> returns to detection.
    /// </summary>
    public static void Set(TerminalCapabilities capabilities) {
        ArgumentNullException.ThrowIfNull(capabilities);
        bool measured = capabilities.CellWidth is > 0 and <= 128 && capabilities.CellHeight is > 0 and <= 256;
        lock (Gate) {
            _overridden = true;
            _attempted = true;
            Volatile.Write(ref _probed, capabilities with {
                CellWidth = measured ? capabilities.CellWidth : 10,
                CellHeight = measured ? capabilities.CellHeight : 20,
                CellSizeMeasured = measured
            });
        }
    }

    /// <summary>Forgets probe results and any <see cref="Set"/> override; the next eligible command probes again.</summary>
    public static void Reset() {
        lock (Gate) {
            _overridden = false;
            _attempted = false;
            Volatile.Write(ref _probed, null);
        }
    }

    private static bool CanProbe(PSCmdlet cmdlet) {
        try {
            if (!string.Equals(cmdlet.Host.Name, "ConsoleHost", StringComparison.OrdinalIgnoreCase))
                return false;
            // Availability is not checked: under pwsh -File the runspace reports Available while the script runs.
            // The call stack excludes the line editor, the prompt and completion, which own input at those times.
            Runspace? runspace = Runspace.DefaultRunspace;
            if (runspace is null)
                return false;
            return !runspace.Debugger.GetCallStack().Any(static frame =>
                frame.FunctionName.Equals("prompt", StringComparison.OrdinalIgnoreCase)
                || frame.FunctionName.Equals("PSConsoleHostReadLine", StringComparison.OrdinalIgnoreCase)
                || frame.FunctionName.StartsWith("TabExpansion", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or PSNotImplementedException) {
            return false;
        }
    }

    internal static TerminalCapabilities FromProbe(TerminalQuery.Result result, Func<string, string?> environment) {
        TerminalCapabilities detected = FromEnvironment(environment);
        string terminal = ReportedTerminal(result.Identity) ?? detected.Terminal;
        if (terminal != detected.Terminal)
            detected = Known(terminal);
        bool measured = result.CellWidth is > 0 and <= 128 && result.CellHeight is > 0 and <= 256;
        return detected with {
            Terminal = terminal,
            // Answers override the environment; iTerm2 inline images have no query and keep the environment's evidence.
            Sixel = result.Sixel ?? detected.Sixel,
            Kitty = result.Kitty ?? detected.Kitty,
            GoodImage = result.GoodImage ?? false,
            CellWidth = measured ? result.CellWidth : 10,
            CellHeight = measured ? result.CellHeight : 20,
            Probed = true,
            CellSizeMeasured = measured
        };
    }

    private static string? ReportedTerminal(string? identity) {
        if (identity is null)
            return null;
        foreach (string name in (ReadOnlySpan<string>)["kitty", "WezTerm", "ghostty", "contour"]) {
            if (identity.StartsWith(name + "(", StringComparison.OrdinalIgnoreCase) || identity.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
                return name.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Environment evidence, in the Sixel module's order: TERM_PROGRAM, terminal variables, then TERM.</summary>
    internal static TerminalCapabilities FromEnvironment(Func<string, string?> environment) {
        string program = environment("TERM_PROGRAM") ?? string.Empty;
        string version = environment("TERM_PROGRAM_VERSION") ?? string.Empty;
        switch (program) {
            case "vscode":
                return Version.TryParse(version.Split('-')[0], out Version? vscode) && vscode >= new Version(1, 102)
                    ? Known("vscode") with { Sixel = true } : Known("vscode");
            case "WezTerm":
                return DateTime.TryParseExact(version.Split('-')[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime build)
                    && build >= new DateTime(2025, 3, 20) ? Known("wezterm") with { Kitty = true, Sixel = true } : Known("wezterm");
            case "iTerm.app":
                return Known("iterm2");
            case "ghostty":
                return Known("ghostty");
            case "mintty":
                return Known("mintty");
        }
        foreach ((string variable, string terminal) in (ReadOnlySpan<(string, string)>)[
            ("WT_SESSION", "windowsterminal"), ("KITTY_WINDOW_ID", "kitty"), ("ITERM_SESSION_ID", "iterm2"),
            ("WEZTERM_CONFIG_FILE", "wezterm"), ("GHOSTTY_RESOURCES_DIR", "ghostty"), ("VSCODE_GIT_ASKPASS_MAIN", "vscode"),
            ("MINTTY", "mintty"), ("CONTOUR_PROFILE", "contour")]) {
            if (!string.IsNullOrEmpty(environment(variable)))
                return Known(terminal);
        }
        return environment("TERM") switch {
            "xterm-kitty" => Known("kitty"),
            "xterm-ghostty" => Known("ghostty"),
            "contour" => Known("contour"),
            "mlterm" => Known("mlterm"),
            _ => Known("unknown")
        };
    }

    private static TerminalCapabilities Known(string terminal) => terminal switch {
        "windowsterminal" or "contour" or "mlterm" => new(terminal, true, false, false, false, 10, 20, false),
        "kitty" or "ghostty" => new(terminal, false, true, false, false, 10, 20, false),
        "iterm2" or "wezterm" or "vscode" or "mintty" => new(terminal, false, false, true, false, 10, 20, false),
        _ => new(terminal, false, false, false, false, 10, 20, false)
    };
}
