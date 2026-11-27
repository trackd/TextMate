using System.Reflection;
using System.Runtime.Loader;

namespace PSTextMate.Core;

/// <summary>Settings shared with optional renderers without referencing their framework types.</summary>
public readonly record struct RendererOptions(bool Alternate = false, string? BaseDirectory = null, int Width = 80, bool? ConstrainWidth = null, int Height = 0);

/// <summary>Implemented by optional rendering assemblies.</summary>
public interface IOutputRenderer {
    object Render(HighlightedDocument document, RendererOptions options);
}

/// <summary>Native output retaining its current shared presentation after container layout.</summary>
public interface ITextMateOutput {
    HighlightedDocument Document { get; }
}

/// <summary>Loads rendering integrations only when explicitly requested.</summary>
public static class OutputRenderer {
    /// <summary>Gets the shared presentation retained by any TextMate output format.</summary>
    public static HighlightedDocument GetDocument(object value) {
        ArgumentNullException.ThrowIfNull(value);
        while (value.GetType().FullName == "System.Management.Automation.PSObject")
            value = UnwrapPowerShell(value);
        return TryGetDocument(value, out HighlightedDocument? document) ? document!
            : throw new ArgumentException("The value is not a TextMate output object.", nameof(value));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object UnwrapPowerShell(object value) => ((PSObject)value).BaseObject;
    private static readonly ConcurrentDictionary<OutputFormat, Lazy<IOutputRenderer>> Renderers = new();
    private static readonly ConcurrentDictionary<OutputFormat, Assembly> Frameworks = new();
    private static readonly ConditionalWeakTable<object, HighlightedDocument> Documents = [];

    internal static void PreferFramework(OutputFormat format, Assembly assembly) {
        if (Frameworks.TryGetValue(format, out Assembly? previous) && ReferenceEquals(previous, assembly))
            return;
        Frameworks[format] = assembly;
        Renderers.TryRemove(format, out _);
    }

    public static object Render(HighlightedDocument document, OutputFormat format, RendererOptions options = default) {
        ArgumentNullException.ThrowIfNull(document);
        if (format == OutputFormat.Text)
            return DocumentPresentation.Prepare(document, options);
        if (format is OutputFormat.Spectre or OutputFormat.Fullscreen) {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                if (assembly.FullName?.StartsWith("System.Management.Automation,", StringComparison.Ordinal) == true) {
                    EnsureConsumer(format);
                    break;
                }
            }
        }
        object result;
        if (format == OutputFormat.Html) {
            document = DocumentPresentation.Prepare(document, options);
            result = HtmlRenderer.Format(document);
            Documents.Add(result, document);
        }
        else {
            result = Get(format).Render(document, options);
            if (result is not ITextMateOutput)
                Documents.Add(result, DocumentPresentation.Prepare(document, options));
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureConsumer(OutputFormat format) => OptionalOutputModule.Ensure(format);

    internal static bool TryGetDocument(object value, out HighlightedDocument? document) {
        if (value is TextContent content) {
            document = content.ToDocument();
            return true;
        }
        if (value is HighlightedDocument existing) {
            document = existing;
            return true;
        }
        if (value is ITextMateOutput native) {
            document = native.Document;
            return true;
        }
        return Documents.TryGetValue(value, out document);
    }

    /// <summary>PowerShell displays native outputs through their common presentation, retaining native pipeline types.</summary>
    public static string Format(object value) {
        while (value is PSObject wrapper)
            value = wrapper.BaseObject;
        return TryGetDocument(value, out HighlightedDocument? document)
            ? TextRenderer.Format(document!)
            : value.ToString() ?? string.Empty;
    }

    public static string Format(object value, int availableWidth) {
        ArgumentOutOfRangeException.ThrowIfLessThan(availableWidth, 1);
        while (value is PSObject wrapper)
            value = wrapper.BaseObject;
        return value is TextContent content ? TextRenderer.Format(content.Layout(availableWidth)) : Format(value);
    }

    internal static IOutputRenderer Get(OutputFormat format) {
        if (format is not (OutputFormat.Spectre or OutputFormat.Fullscreen))
            throw new ArgumentOutOfRangeException(nameof(format));
        Lazy<IOutputRenderer> renderer = Renderers.GetOrAdd(format, static key => new Lazy<IOutputRenderer>(() => Load(key)));
        try {
            return renderer.Value;
        }
        catch {
            // Installing/importing an optional runtime after a failed request must allow a retry.
            Renderers.TryRemove(new KeyValuePair<OutputFormat, Lazy<IOutputRenderer>>(format, renderer));
            throw;
        }
    }

    private static IOutputRenderer Load(OutputFormat format) {
        string backend = format == OutputFormat.Fullscreen ? "XenoAtom" : format.ToString();
        string name = "PSTextMate." + backend;
        string directory = Path.Combine(Path.GetDirectoryName(typeof(OutputRenderer).Assembly.Location)!, "renderers", backend);
        string path = Path.Combine(directory, name + ".dll");
        if (!File.Exists(path)) {
            string adjacent = Path.Combine(Path.GetDirectoryName(typeof(OutputRenderer).Assembly.Location)!, name + ".dll");
            if (File.Exists(adjacent)) {
                path = adjacent;
                directory = Path.GetDirectoryName(adjacent)!;
            }
        }
        if (!File.Exists(path)) {
            throw new InvalidOperationException(
            $"The optional {format} renderer is not installed. Expected '{path}'. Use Text output or install the renderer.");
        }

        Frameworks.TryGetValue(format, out Assembly? preferred);
        var context = new RendererLoadContext(directory, preferred);
        string framework = format == OutputFormat.Spectre ? "Spectre.Console" : "XenoAtom.Terminal.UI";
        Assembly assembly = context.LoadFromAssemblyPath(path);
        AssemblyName frameworkName = new(framework);
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies()) {
            if (reference.Name == framework) {
                frameworkName = reference;
                break;
            }
        }
        try {
            context.LoadFromAssemblyName(frameworkName);
        }
        catch (FileNotFoundException error) {
            string module = format == OutputFormat.Spectre ? "PwshSpectreConsole" : "fullscreen";
            throw new InvalidOperationException($"{format} output requires {framework}. Install/import the {module} module, or build TextMate with -Renderer {format} to bundle the runtime.", error);
        }
        return (IOutputRenderer)Activator.CreateInstance(assembly.GetType(name + ".Renderer", throwOnError: true)!)!;
    }

    internal static T LoadPresentation<T>(string name) {
        string directory = Path.GetDirectoryName(typeof(OutputRenderer).Assembly.Location)!;
        string pluginDirectory = Path.Combine(directory, "presentation", name);
        string assemblyName = "PSTextMate." + name;
        string path = Path.Combine(pluginDirectory, assemblyName + ".dll");
        if (!File.Exists(path)) {
            pluginDirectory = directory;
            path = Path.Combine(directory, assemblyName + ".dll");
        }
        if (!File.Exists(path))
            throw new InvalidOperationException($"The shared {name} presentation assembly is missing: '{path}'.");
        var context = new RendererLoadContext(pluginDirectory);
        Assembly assembly = context.LoadFromAssemblyPath(path);
        return (T)Activator.CreateInstance(assembly.GetType(assemblyName + ".Compiler", throwOnError: true)!)!;
    }

    internal static bool HasPresentation(string name) {
        string directory = Path.GetDirectoryName(typeof(OutputRenderer).Assembly.Location)!;
        return File.Exists(Path.Combine(directory, "presentation", name, "PSTextMate." + name + ".dll"))
            || File.Exists(Path.Combine(directory, "PSTextMate." + name + ".dll"));
    }

    private sealed class RendererLoadContext(string directory, Assembly? framework = null) : AssemblyLoadContext {
        protected override Assembly? Load(AssemblyName name) {
            Assembly core = typeof(OutputRenderer).Assembly;
            if (name.Name == core.GetName().Name)
                return core;
            if (framework is not null && name.Name is { } requested
                && (requested.StartsWith("Spectre.", StringComparison.Ordinal) || requested.StartsWith("XenoAtom.", StringComparison.Ordinal))) {
                return GetLoadContext(framework)!.LoadFromAssemblyName(name);
            }
            // Share the core and engine, and the consumer's rendering framework, across ALCs.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                AssemblyName loaded = assembly.GetName();
                if (!AssemblyName.ReferenceMatchesDefinition(loaded, name))
                    continue;
                if (!(loaded.GetPublicKeyToken() ?? []).AsSpan().SequenceEqual(name.GetPublicKeyToken() ?? []))
                    continue;
                if (name.Version is null || loaded.Version == name.Version)
                    return assembly;
            }
            string path = Path.Combine(directory, name.Name + ".dll");
            if (!File.Exists(path) && name.Name == "Markdig.Signed") {
                string root = Path.GetDirectoryName(typeof(OutputRenderer).Assembly.Location)!;
                path = Path.Combine(root, "presentation", "Markdown", name.Name + ".dll");
            }
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
