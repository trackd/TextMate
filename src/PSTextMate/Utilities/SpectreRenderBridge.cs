namespace PSTextMate.Utilities;

/// <summary>
/// Provides an ALC-safe bridge for rendering Spectre objects to plain text.
/// </summary>
public static class SpectreRenderBridge {
    private static readonly CallSite<Func<CallSite, object, IRenderable>> s_convertToRenderableCallSite =
        CreateConvertToRenderableCallSite();

    /// <summary>
    /// Renders a Spectre renderable object to a string.
    /// </summary>
    /// <param name="renderableObject">Object implementing <see cref="IRenderable"/>.</param>
    /// <param name="escapeAnsi">When true, strips ANSI sequences from the output.</param>
    /// <returns>The rendered string output.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="renderableObject"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="renderableObject"/> does not implement <see cref="IRenderable"/>.</exception>
    public static string RenderToString(object renderableObject, bool escapeAnsi = false, int? width = null) {
        ArgumentNullException.ThrowIfNull(renderableObject);

        string rendered = renderableObject is IRenderable localRenderable
            ? RenderLocal(localRenderable, width)
            : RenderForeign(renderableObject, width);

        return rendered.Length != 0
            ? escapeAnsi ? VTHelpers.StripAnsi(rendered) : rendered
            : throw new ArgumentException(
                $"Object of type '{renderableObject.GetType().FullName}' does not implement a supported Spectre IRenderable shape.",
                nameof(renderableObject)
            );
    }

    /// <summary>
    /// Attempts to convert a foreign Spectre renderable object to the local <see cref="IRenderable"/> type.
    /// </summary>
    /// <param name="value">The candidate renderable object.</param>
    /// <param name="renderable">Converted renderable when conversion succeeds.</param>
    /// <returns><see langword="true"/> when conversion succeeds; otherwise <see langword="false"/>.</returns>
    public static bool TryConvertToLocalRenderable(
        object value,
        [NotNullWhen(true)] out IRenderable? renderable
    ) {
        ArgumentNullException.ThrowIfNull(value);

        if (value is IRenderable local) {
            renderable = local;
            return true;
        }

        string? fullName = value.GetType().FullName;
        if (string.IsNullOrWhiteSpace(fullName)
            || !fullName.StartsWith("Spectre.Console.", StringComparison.Ordinal)) {
            renderable = null;
            return false;
        }

        try {
            renderable = s_convertToRenderableCallSite.Target(s_convertToRenderableCallSite, value);
            return renderable is not null;
        }
        catch (RuntimeBinderException) {
            renderable = null;
            return false;
        }
        catch (InvalidCastException) {
            renderable = null;
        }

        if (TryCreateForeignRenderableAdapter(value, out IRenderable? adaptedRenderable)) {
            renderable = adaptedRenderable;
            return true;
        }

        return false;
    }

    private static string RenderLocal(IRenderable renderable, int? width) {
        using StringWriter writer = new(new StringBuilder(1024), CultureInfo.InvariantCulture);
        var output = new AnsiConsoleOutput(writer);
        var settings = new AnsiConsoleSettings { Out = output };
        IAnsiConsole console = AnsiConsole.Create(settings);
        if (width is int targetWidth && targetWidth > 0) {
            console.Profile.Width = targetWidth;
        }

        console.Write(renderable);
        return writer.ToString();
    }

    private static string RenderForeign(object renderableObject, int? width) {
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
            || foreignRenderableType?.IsInstanceOfType(renderableObject) != true) {
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

        if (width is int targetWidth && targetWidth > 0) {
            PropertyInfo? profileProperty = console.GetType().GetProperty("Profile");
            object? profile = profileProperty?.GetValue(console);
            PropertyInfo? widthProperty = profile?.GetType().GetProperty("Width");
            if (widthProperty?.CanWrite == true) {
                widthProperty.SetValue(profile, targetWidth);
            }
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

    private static CallSite<Func<CallSite, object, IRenderable>> CreateConvertToRenderableCallSite() {
        return CallSite<Func<CallSite, object, IRenderable>>.Create(
            Microsoft.CSharp.RuntimeBinder.Binder.Convert(
                CSharpBinderFlags.ConvertExplicit,
                typeof(IRenderable),
                typeof(SpectreRenderBridge)
            )
        );
    }

    private static bool TryCreateForeignRenderableAdapter(
        object value,
        [NotNullWhen(true)] out IRenderable? renderable
    ) {
        Type valueType = value.GetType();
        string? fullName = valueType.FullName;
        if (string.IsNullOrWhiteSpace(fullName)
            || !fullName.StartsWith("Spectre.Console.", StringComparison.Ordinal)) {
            renderable = null;
            return false;
        }

        Type? foreignRenderableType = valueType.Assembly.GetType("Spectre.Console.Rendering.IRenderable")
            ?? valueType.Assembly.GetType("Spectre.Console.IRenderable");
        if (foreignRenderableType?.IsInstanceOfType(value) != true) {
            renderable = null;
            return false;
        }

        renderable = new ForeignRenderableAdapter(value);
        return true;
    }

    private static IRenderable ConvertAnsiToRenderable(string ansi) {
        string[] lines = ansi.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        if (lines.Length <= 1) {
            return Helpers.VTConversion.ToParagraph(lines[0]);
        }

        var renderables = new IRenderable[lines.Length];
        for (int i = 0; i < lines.Length; i++) {
            renderables[i] = Helpers.VTConversion.ToParagraph(lines[i]);
        }

        return new Rows(renderables);
    }

    private sealed class ForeignRenderableAdapter : IRenderable {
        private readonly object _foreignRenderable;
        private int _cachedWidth;
        private IRenderable? _cachedRenderable;

        public ForeignRenderableAdapter(object foreignRenderable) {
            _foreignRenderable = foreignRenderable;
            _cachedWidth = -1;
        }

        public Measurement Measure(RenderOptions options, int maxWidth)
            => GetOrCreate(maxWidth).Measure(options, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
            => GetOrCreate(maxWidth).Render(options, maxWidth);

        private IRenderable GetOrCreate(int maxWidth) {
            int width = Math.Max(1, maxWidth);
            if (_cachedRenderable is not null && _cachedWidth == width) {
                return _cachedRenderable;
            }

            string rendered = RenderToString(_foreignRenderable, width: width);
            _cachedRenderable = ConvertAnsiToRenderable(rendered);
            _cachedWidth = width;
            return _cachedRenderable;
        }
    }
}
