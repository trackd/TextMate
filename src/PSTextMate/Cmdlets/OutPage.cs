namespace PSTextMate.Commands;

/// <summary>
/// Sends renderables or VT-formatted strings to the interactive pager.
/// </summary>
[Cmdlet(VerbsData.Out, "Page")]
[OutputType(typeof(void))]
public sealed class OutPageCmdlet : PSCmdlet {
    private readonly List<IRenderable> _renderables = [];
    private readonly List<object> _outStringInputs = [];
    private HighlightedText? _singleHighlightedText;
    private bool _sawNonHighlightedInput;

    /// <summary>
    /// Pipeline input to page.
    /// Accepts <see cref="IRenderable"/> values directly, or strings that are
    /// converted through <see cref="Helpers.VTConversion.ToParagraph(string)"/>.
    /// </summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [AllowNull]
    public PSObject? InputObject { get; set; }

    /// <summary>
    /// Processes one input object from the pipeline.
    /// </summary>
    protected override void ProcessRecord() {
        if (InputObject?.BaseObject is null) {
            WriteVerbose("ProcessRecord: InputObject is null; skipping item.");
            return;
        }

        object value = InputObject.BaseObject;
        WriteVerbose($"ProcessRecord: received input type '{value.GetType().FullName}'.");

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
            string rendered = Writer.WriteToString(renderable, width: GetConsoleWidth());
            if (!string.IsNullOrEmpty(rendered)) {
                AddParagraphLines(_renderables, rendered);
                WriteVerbose("ProcessRecord: input matched IRenderable and was expanded into paragraph lines.");
            }
            else {
                _renderables.Add(renderable);
                WriteVerbose("ProcessRecord: input matched IRenderable and was added as-is.");
            }

            return;
        }

        if (value is string text) {
            _outStringInputs.Add(text);
            WriteVerbose("ProcessRecord: input matched string; queued for Out-String conversion.");
            return;
        }

        if (value is Renderable renderable1) {
            _renderables.Add(renderable1);
            WriteVerbose("ProcessRecord: input matched Renderable concrete type; added directly.");
            return;
        }

        try {
            var r = (Renderable)value;
            if (r is not null) {
                _renderables.Add(r);
                WriteVerbose("ProcessRecord: input cast to Renderable successfully; added directly.");
                return;
            }
        }
        catch {
            WriteVerbose("ProcessRecord: direct Renderable cast failed; continuing with foreign conversion attempt.");
        }

        if (TryConvertForeignSpectreRenderable(value, out List<IRenderable> convertedRenderables)) {
            _renderables.AddRange(convertedRenderables);
            WriteVerbose($"ProcessRecord: converted foreign Spectre renderable into {convertedRenderables.Count} line renderables.");
            return;
        }

        _outStringInputs.Add(value);
        WriteVerbose("ProcessRecord: no renderable conversion path matched; queued object for Out-String conversion.");
    }

    /// <summary>
    /// Runs the pager when all pipeline input has been collected.
    /// </summary>
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
                    _renderables.Add(Helpers.VTConversion.ToParagraph(line));
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
            .AddParameter("Width", GetConsoleWidth());

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

    private static void AddLines(List<string> lines, string text) {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        foreach (string line in normalized.Split('\n')) {
            lines.Add(line);
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

    private static bool TryConvertForeignSpectreRenderable(object value, out List<IRenderable> renderables) {
        renderables = [];

        Type valueType = value.GetType();
        string? fullName = valueType.FullName;
        if (string.IsNullOrWhiteSpace(fullName)
            || !fullName.StartsWith("Spectre.Console.", StringComparison.Ordinal)
            || value is IRenderable) {
            return false;
        }

        string ansi = RenderForeignSpectreToAnsi(value);
        if (string.IsNullOrEmpty(ansi)) {
            return false;
        }

        renderables = ConvertAnsiToLineRenderables(ansi);
        return renderables.Count != 0;
    }

    private static List<IRenderable> ConvertAnsiToLineRenderables(string ansi) {
        string[] lines = ansi.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var renderables = new List<IRenderable>(lines.Length);

        foreach (string line in lines) {
            renderables.Add(Helpers.VTConversion.ToParagraph(line));
        }

        return renderables;
    }

    private static void AddParagraphLines(List<IRenderable> destination, string ansi) {
        if (string.IsNullOrEmpty(ansi)) {
            return;
        }

        List<IRenderable> lines = ConvertAnsiToLineRenderables(ansi);
        if (lines.Count == 0) {
            return;
        }

        destination.AddRange(lines);
    }

    private static string RenderForeignSpectreToAnsi(object value) {
        try {
            Assembly assembly = value.GetType().Assembly;
            Type? ansiConsoleType = assembly.GetType("Spectre.Console.AnsiConsole");
            Type? ansiConsoleSettingsType = assembly.GetType("Spectre.Console.AnsiConsoleSettings");
            Type? ansiConsoleOutputType = assembly.GetType("Spectre.Console.AnsiConsoleOutput");
            Type? renderableType = assembly.GetType("Spectre.Console.Rendering.IRenderable")
                ?? assembly.GetType("Spectre.Console.IRenderable");

            if (ansiConsoleType is null
                || ansiConsoleSettingsType is null
                || ansiConsoleOutputType is null
                || renderableType?.IsInstanceOfType(value) != true) {
                return string.Empty;
            }

            using StringWriter writer = new(new StringBuilder(1024), CultureInfo.InvariantCulture);
            object? output = Activator.CreateInstance(ansiConsoleOutputType, writer);
            object? settings = Activator.CreateInstance(ansiConsoleSettingsType);
            PropertyInfo? outProperty = ansiConsoleSettingsType.GetProperty("Out");

            if (output is null || settings is null || outProperty is null || !outProperty.CanWrite) {
                return string.Empty;
            }

            outProperty.SetValue(settings, output);

            MethodInfo? createMethod = ansiConsoleType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method => method.Name == "Create"
                    && method.GetParameters() is { Length: 1 } parameters
                    && parameters[0].ParameterType == ansiConsoleSettingsType);

            object? console = createMethod?.Invoke(null, [settings]);
            if (console is null) {
                return string.Empty;
            }

            MethodInfo? writeMethod = console.GetType().GetMethod("Write", [renderableType]);
            if (writeMethod is not null) {
                _ = writeMethod.Invoke(console, [value]);
            }
            else {
                Type? extType = assembly.GetType("Spectre.Console.AnsiConsoleExtensions");
                MethodInfo? extWriteMethod = extType?
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "Write"
                        && method.GetParameters() is { Length: 2 } parameters
                        && parameters[1].ParameterType == renderableType);

                if (extWriteMethod is null) {
                    return string.Empty;
                }

                _ = extWriteMethod.Invoke(null, [console, value]);
            }

            return writer.ToString();
        }
        catch {
            return string.Empty;
        }
    }

}
