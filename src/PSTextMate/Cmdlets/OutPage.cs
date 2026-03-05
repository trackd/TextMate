namespace PSTextMate.Commands;

/// <summary>
/// Sends renderables or VT-formatted strings to the interactive pager.
/// </summary>
[Cmdlet(VerbsData.Out, "Page")]
[OutputType(typeof(void))]
public sealed class OutPageCmdlet : PSCmdlet {
    private readonly List<IRenderable> _renderables = [];
    private readonly List<object> _outStringInputs = [];

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
            return;
        }

        object value = InputObject.BaseObject;

        if (value is IRenderable renderable) {
            _renderables.Add(renderable);
            return;
        }

        if (value is string text) {
            _outStringInputs.Add(text);
            return;
        }

        if (TryConvertForeignSpectreRenderable(value, out Paragraph paragraph)) {
            _renderables.Add(paragraph);
            return;
        }

        _outStringInputs.Add(value);
    }

    /// <summary>
    /// Runs the pager when all pipeline input has been collected.
    /// </summary>
    protected override void EndProcessing() {
        if (_outStringInputs.Count > 0) {
            string formatted = ConvertWithOutString(_outStringInputs);
            if (!string.IsNullOrEmpty(formatted)) {
                _renderables.Add(Helpers.VTConversion.ToParagraph(formatted));
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

        using var pager = new Pager(_renderables);
        pager.Show();
    }

    private static string ConvertWithOutString(List<object> values) {
        if (values.Count == 0) {
            return string.Empty;
        }

        OutputRendering previousOutputRendering = PSStyle.Instance.OutputRendering;
        try {
            PSStyle.Instance.OutputRendering = OutputRendering.Ansi;

            using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
            ps.AddCommand("Out-String")
            .AddParameter("Width", GetConsoleWidth());

            Collection<PSObject> results = ps.Invoke(values);
            if (ps.HadErrors || results.Count == 0) {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (PSObject? result in results) {
                if (result?.BaseObject is string text) {
                    builder.Append(text);
                }
                else {
                    builder.Append(result?.ToString());
                }
            }

            return builder.ToString();
        }
        catch {
            return string.Empty;
        }
        finally {
            PSStyle.Instance.OutputRendering = previousOutputRendering;
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

    private static bool TryConvertForeignSpectreRenderable(object value, out Paragraph paragraph) {
        paragraph = new Paragraph();

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

        paragraph = Helpers.VTConversion.ToParagraph(ansi);
        return true;
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
                || renderableType is null
                || !renderableType.IsInstanceOfType(value)) {
                return string.Empty;
            }

            using var writer = new StringWriter();
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
