namespace PSTextMate.Commands;

/// <summary>
/// Sends renderables or VT-formatted strings to the interactive pager.
/// </summary>
[Cmdlet(VerbsData.Out, "Page")]
[OutputType(typeof(void))]
public sealed class OutPageCmdlet : PSCmdlet {
    private const char Escape = '\x1B';
    private readonly List<IRenderable> _renderables = [];
    private readonly List<object> _outStringInputs = [];
    private HighlightedText? _singleHighlightedText;
    private bool _sawNonHighlightedInput;

    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [System.Management.Automation.AllowNull]
    public PSObject? InputObject { get; set; }

    protected override void ProcessRecord() {
        if (InputObject?.BaseObject is null) {
            WriteVerbose("ProcessRecord: InputObject is null; skipping item.");
            return;
        }

        object value = InputObject.BaseObject;
        WriteVerbose($"ProcessRecord: received input type '{value.GetType().FullName}' BaseType: '{value.GetType().BaseType}'.");

        if (value is HighlightedText highlightedText) {
            if (_singleHighlightedText is null && !_sawNonHighlightedInput && _renderables.Count == 0 && _outStringInputs.Count == 0) {
                _singleHighlightedText = highlightedText;
                WriteVerbose("ProcessRecord: took HighlightedText fast path and deferred to highlighted pager.");
                return;
            }

            _sawNonHighlightedInput = true;
            _renderables.AddRange(highlightedText.Renderables);
            WriteVerbose("ProcessRecord: merged HighlightedText renderables into the main renderable list.");
            return;
        }

        _sawNonHighlightedInput = true;

        if (value is IRenderable renderable) {
            _renderables.Add(renderable);
            WriteVerbose("ProcessRecord: input matched IRenderable and was added directly.");
            return;
        }

        if (value is string text) {
            _outStringInputs.Add(text);
            WriteVerbose("ProcessRecord: input matched string; queued for Out-String conversion.");
            return;
        }

        if (TryConvertForeignSpectreRenderable(value, out IRenderable? convertedRenderable)) {
            _renderables.Add(convertedRenderable);
            WriteVerbose("ProcessRecord: converted foreign Spectre renderable to local IRenderable.");
            return;
        }

        if (IsSpectreObject(value)) {
            string localAssembly = typeof(IRenderable).Assembly.FullName ?? "<unknown>";
            string foreignAssembly = value.GetType().Assembly.FullName ?? "<unknown>";
            WriteVerbose(
                $"ProcessRecord: Spectre object conversion failed (local='{localAssembly}', foreign='{foreignAssembly}'). "
                + "This usually indicates duplicate Spectre.Console assemblies loaded in different contexts; restart the session after updating the module."
            );
        }

        _outStringInputs.Add(value);
        WriteVerbose("ProcessRecord: no renderable conversion path matched; queued object for Out-String conversion.");
    }

    protected override void EndProcessing() {
        if (_singleHighlightedText is not null && !_sawNonHighlightedInput && _renderables.Count == 0 && _outStringInputs.Count == 0) {
            WriteVerbose("EndProcessing: using single HighlightedText pager path.");
            using var highlightedPager = new Pager(_singleHighlightedText);
            highlightedPager.Show();
            return;
        }

        if (_outStringInputs.Count > 0) {
            WriteVerbose($"EndProcessing: converting {_outStringInputs.Count} queued value(s) through Out-String.");
            List<string> formattedLines = ConvertWithOutStringLines(_outStringInputs);
            if (formattedLines.Count > 0) {
                foreach (string line in formattedLines) {
                    _renderables.Add(line.Length == 0
                        ? new Text(string.Empty)
                        : Helpers.VTConversion.ToParagraph(line));
                }

                WriteVerbose($"EndProcessing: Out-String produced {formattedLines.Count} line(s) for paging.");
            }
            else {
                foreach (object value in _outStringInputs) {
                    _renderables.Add(new Text(LanguagePrimitives.ConvertTo<string>(value)));
                }

                WriteVerbose("EndProcessing: Out-String returned no lines; used string conversion fallback.");
            }
        }

        if (_renderables.Count == 0) {
            WriteVerbose("EndProcessing: no renderables collected; nothing to page.");
            return;
        }

        WriteVerbose($"EndProcessing: launching pager with {_renderables.Count} renderable(s).");
        using var pager = new Pager(_renderables);
        pager.Show();
    }

    private static List<string> ConvertWithOutStringLines(List<object> values) {
        if (values.Count == 0) {
            return [];
        }

        OutputRendering previousOutputRendering = PSStyle.Instance.OutputRendering;
        try {
            PSStyle.Instance.OutputRendering = OutputRendering.Ansi;

            using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
            ps.AddCommand("Out-String")
            .AddParameter("Stream")
            .AddParameter("Width", GetOutStringWidth());

            Collection<PSObject> results = ps.Invoke(values);
            if (ps.HadErrors || results.Count == 0) {
                return [];
            }

            var lines = new List<string>(results.Count);
            foreach (PSObject? result in results) {
                if (result?.BaseObject is string text) {
                    AddLines(lines, text);
                }
                else {
                    AddLines(lines, result?.ToString() ?? string.Empty);
                }
            }

            // TrimBoundaryEmptyLines(lines);
            return lines;
        }
        catch {
            return [];
        }
        finally {
            PSStyle.Instance.OutputRendering = previousOutputRendering;
        }
    }

    private static void AddLines(List<string> lines, string text) {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        string[] split = normalized.Split('\n');
        int count = split.Length;

        // Out-String -Stream commonly returns one chunk per logical line with a
        // trailing newline terminator. Ignore only that terminator-induced empty
        // entry so pager row counts match what is actually rendered.
        if (count > 0 && split[^1].Length == 0 && normalized.EndsWith('\n')) {
            count--;
        }

        for (int i = 0; i < count; i++) {
            lines.Add(split[i]);
        }
    }

    private static int GetConsoleWidth() {
        try {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 120;
        }
        catch {
            return 120;
        }
    }

    // Keep one-column slack so width-bound lines from Out-String do not
    // wrap in the live pager viewport and skew row-height calculations.
    private static int GetOutStringWidth() => Math.Max(20, GetConsoleWidth() - 1);

    private static bool TryConvertForeignSpectreRenderable(
        object value,
        [NotNullWhen(true)] out IRenderable? renderable
    ) {
        renderable = null;

        Type valueType = value.GetType();
        string? fullName = valueType.FullName;
        return IsSpectreObject(fullName)
            && value is not IRenderable
            && SpectreRenderBridge.TryConvertToLocalRenderable(value, out renderable);
    }

    private static bool IsSpectreObject(object value) {
        string? fullName = value.GetType().FullName;
        return IsSpectreObject(fullName);
    }
    private static bool IsSpectreObject(string? str) {
        return !string.IsNullOrWhiteSpace(str)
            && (str.StartsWith("Spectre.Console.", StringComparison.Ordinal) ||
            str.StartsWith("PwshSpectreConsole.", StringComparison.Ordinal));
    }
}
