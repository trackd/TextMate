using System;
using System.IO;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace PSTextMate.ALC;

/// <summary>Isolates the managed highlighting engine from other PowerShell modules.</summary>
public sealed class LoadContext : AssemblyLoadContext {
    private static readonly Lock Sync = new();
    private static LoadContext? _instance;
    private readonly string _directory;
    private readonly Assembly _module;

    private LoadContext(string directory) : base("PSTextMate", isCollectible: false) {
        _directory = directory;
        _module = LoadFromAssemblyPath(Path.Combine(directory, "PSTextMate.dll"));
    }

    protected override Assembly? Load(AssemblyName name) {
        if (name.Name == typeof(PSObject).Assembly.GetName().Name)
            return typeof(PSObject).Assembly;
        if (name.Name == typeof(LoadContext).Assembly.GetName().Name)
            return typeof(LoadContext).Assembly;
        string path = Path.Combine(_directory, name.Name + ".dll");
        return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }

    /// <summary>Loads the module once without loading grammars, themes or optional renderers.</summary>
    public static Assembly Initialize() {
        lock (Sync) {
            _instance ??= new LoadContext(Path.GetDirectoryName(typeof(LoadContext).Assembly.Location)
                ?? throw new InvalidOperationException("Cannot locate the TextMate module."));
            return _instance._module;
        }
    }
}
