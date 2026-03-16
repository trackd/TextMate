namespace PSTextMate.Commands;

/// <summary>
/// Sends renderables or VT-formatted strings to the interactive pager.
/// </summary>
[Cmdlet(VerbsData.Out, "Page")]
[Alias("page")]
[OutputType(typeof(void))]
public sealed class OutPageCmdlet : PSCmdlet {
    private readonly List<IRenderable> _renderables = [];
    private readonly List<object> _outStringInputs = [];
    private HighlightedText? _singleHighlightedText;
    private bool _sawNonHighlightedInput;

    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [System.Management.Automation.AllowNull]
    public PSObject? InputObject { get; set; }

    protected override void ProcessRecord() {
        if (InputObject?.BaseObject is null) {
            return;
        }

        object value = InputObject.BaseObject;

        if (value is HighlightedText highlightedText) {
            if (_singleHighlightedText is null && !_sawNonHighlightedInput && _renderables.Count == 0 && _outStringInputs.Count == 0) {
                _singleHighlightedText = highlightedText;
                return;
            }

            _sawNonHighlightedInput = true;
            _renderables.AddRange(highlightedText.Renderables);
            return;
        }

        _sawNonHighlightedInput = true;

        if (value is IRenderable renderable) {
            _renderables.Add(renderable);
            return;
        }

        if (value is string text) {
            _outStringInputs.Add(text);
            return;
        }

        if (TryConvertForeignSpectreRenderable(value, out IRenderable? convertedRenderable)) {
            _renderables.Add(convertedRenderable);
            return;
        }

        _outStringInputs.Add(InputObject);
    }

    protected override void EndProcessing() {
        if (_singleHighlightedText is not null && !_sawNonHighlightedInput && _renderables.Count == 0 && _outStringInputs.Count == 0) {
            var highlightedPager = new Pager(_singleHighlightedText);
            highlightedPager.Show();
            return;
        }

        if (_outStringInputs.Count > 0) {
            List<string> formattedLines = ConvertWithOutStringLines(_outStringInputs);
            if (formattedLines.Count > 0) {
                foreach (string line in formattedLines) {
                    _renderables.Add(line.Length == 0 ? Text.Empty : VTConversion.ToParagraph(line));
                }

            }
            else {
                foreach (object value in _outStringInputs) {
                    _renderables.Add(new Text(LanguagePrimitives.ConvertTo<string>(value)));
                }

            }
        }

        if (_renderables.Count == 0) {
            return;
        }

        var pager = new Pager(_renderables, AnsiConsole.Console, null, suppressTerminalControlSequences: false);
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

            return lines;
        }
        catch {
            return [];
        }
        finally {
            PSStyle.Instance.OutputRendering = previousOutputRendering;
        }
    }

    // Out-String -Stream commonly returns one chunk per logical line with a
    // trailing newline terminator. Trim only that final synthetic empty line.
    private static void AddLines(List<string> lines, string text) =>

        TextMateHelper.AddSplitLines(lines, text, trimTrailingTerminatorEmptyLine: true);

    private static int GetConsoleWidth() {
        try {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 120;
        }
        catch {
            return 120;
        }
    }

    private static int GetOutStringWidth() => Math.Max(20, GetConsoleWidth() - 5);

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
    private static bool IsSpectreObject(string? str) {
        return !string.IsNullOrWhiteSpace(str)
            && (str.StartsWith("Spectre.Console.", StringComparison.Ordinal) ||
            str.StartsWith("PwshSpectreConsole.", StringComparison.Ordinal));
    }
}
