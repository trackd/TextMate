namespace PSTextMate.Utilities;

/// <summary>
/// Provides an ALC-safe bridge for rendering Spectre objects to plain text.
/// </summary>
public static class SpectreRenderBridge {
    /// <summary>
    /// Renders a Spectre renderable object to a string.
    /// </summary>
    /// <param name="renderableObject">Object implementing <see cref="IRenderable"/>.</param>
    /// <param name="escapeAnsi">When true, strips ANSI sequences from the output.</param>
    /// <returns>The rendered string output.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="renderableObject"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="renderableObject"/> does not implement <see cref="IRenderable"/>.</exception>
    public static string RenderToString(object renderableObject, bool escapeAnsi = false) {
        ArgumentNullException.ThrowIfNull(renderableObject);

        string rendered = renderableObject is IRenderable localRenderable
            ? RenderLocal(localRenderable)
            : RenderForeign(renderableObject);

        if (string.IsNullOrEmpty(rendered)) {
            throw new ArgumentException(
                $"Object of type '{renderableObject.GetType().FullName}' does not implement a supported Spectre IRenderable shape.",
                nameof(renderableObject)
            );
        }

        return escapeAnsi ? PSHostUserInterface.GetOutputString(rendered, false) : rendered;
    }

    private static string RenderLocal(IRenderable renderable) {
        using StringWriter writer = new(new StringBuilder(1024), CultureInfo.InvariantCulture);
        var output = new AnsiConsoleOutput(writer);
        var settings = new AnsiConsoleSettings { Out = output };
        IAnsiConsole console = AnsiConsole.Create(settings);
        console.Write(renderable);
        return writer.ToString();
    }

    private static string RenderForeign(object renderableObject) {
        Type valueType = renderableObject.GetType();
        Assembly assembly = valueType.Assembly;

        Type? ansiConsoleType = assembly.GetType("Spectre.Console.AnsiConsole");
        Type? ansiConsoleSettingsType = assembly.GetType("Spectre.Console.AnsiConsoleSettings");
        Type? ansiConsoleOutputType = assembly.GetType("Spectre.Console.AnsiConsoleOutput");
        Type? foreignRenderableType = assembly.GetType("Spectre.Console.Rendering.IRenderable")
            ?? assembly.GetType("Spectre.Console.IRenderable");

        if (ansiConsoleType is null
            || ansiConsoleSettingsType is null
            || ansiConsoleOutputType is null
            || foreignRenderableType is null
            || !foreignRenderableType.IsInstanceOfType(renderableObject)) {
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

        MethodInfo? writeMethod = console.GetType().GetMethod("Write", [foreignRenderableType]);
        if (writeMethod is not null) {
            _ = writeMethod.Invoke(console, [renderableObject]);
            return writer.ToString();
        }

        Type? extType = assembly.GetType("Spectre.Console.AnsiConsoleExtensions");
        MethodInfo? extWriteMethod = extType?
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "Write"
                && method.GetParameters() is { Length: 2 } parameters
                && parameters[1].ParameterType == foreignRenderableType);
        if (extWriteMethod is null) {
            return string.Empty;
        }

        _ = extWriteMethod.Invoke(null, [console, renderableObject]);
        return writer.ToString();
    }
}
