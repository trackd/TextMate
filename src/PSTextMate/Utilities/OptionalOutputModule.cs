using System.Management.Automation.Runspaces;
using System.Reflection;

namespace PSTextMate.Utilities;

/// <summary>Loads an installed consumer only when its native output is explicitly requested.</summary>
internal static class OptionalOutputModule {
    internal static void Ensure(OutputFormat format) {
        string framework;
        string module;
        switch (format) {
            case OutputFormat.Spectre:
                framework = "Spectre.Console";
                module = "PwshSpectreConsole";
                break;
            case OutputFormat.Fullscreen:
                framework = "XenoAtom.Terminal.UI";
                module = "fullscreen";
                break;
            default:
                return;
        }
        if (Runspace.DefaultRunspace is null)
            return;
        using var pipeline = PowerShell.Create(RunspaceMode.CurrentRunspace);
        pipeline.AddCommand("Microsoft.PowerShell.Core\\Get-Module").AddParameter("Name", module);
        Collection<PSObject> modules = pipeline.Invoke();
        try {
            if (modules.Count == 0) {
                pipeline.Commands.Clear();
                pipeline.AddCommand("Microsoft.PowerShell.Core\\Get-Module").AddParameter("Name", module).AddParameter("ListAvailable");
                if (pipeline.Invoke().Count == 0)
                    return;
                pipeline.Commands.Clear();
                pipeline.Streams.Error.Clear();
                pipeline.AddCommand("Microsoft.PowerShell.Core\\Import-Module").AddParameter("Name", module)
                    .AddParameter("PassThru").AddParameter("ErrorAction", ActionPreference.Stop);
                modules = pipeline.Invoke();
            }
            foreach (PSObject value in modules) {
                if (value.BaseObject is not PSModuleInfo info)
                    continue;
                string root = Path.GetFullPath(info.ModuleBase) + Path.DirectorySeparatorChar;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    if (assembly.IsDynamic || assembly.GetName().Name != framework || string.IsNullOrEmpty(assembly.Location))
                        continue;
                    if (assembly.Location.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                        OutputRenderer.PreferFramework(format, assembly);
                        return;
                    }
                }
            }
        }
        catch (RuntimeException error) when (error.ErrorRecord.FullyQualifiedErrorId.StartsWith("Modules_ModuleNotFound", StringComparison.Ordinal)) {
            // A package built with -Renderer may supply the framework without a PowerShell consumer.
        }
    }
}
