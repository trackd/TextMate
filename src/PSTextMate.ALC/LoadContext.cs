#if NET5_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace PSTextMate.ALC;

/// <summary>
/// Custom AssemblyLoadContext for isolating and resolving assemblies in .NET 5.0 or greater environments.
/// </summary>
public class LoadContext : AssemblyLoadContext {
    private static LoadContext? _instance;
    private static readonly object _sync = new();

    private readonly Assembly _thisAssembly;
    private readonly AssemblyName _thisAssemblyName;
    private readonly Assembly _moduleAssembly;
    private readonly string _assemblyDir;
    private readonly string[] _nativeProbeDirs;

    private LoadContext(string mainModulePathAssemblyPath)
        : base(name: "PSTextMate", isCollectible: false) {
        _assemblyDir = Path.GetDirectoryName(mainModulePathAssemblyPath) ?? "";
        _thisAssembly = typeof(LoadContext).Assembly;
        _thisAssemblyName = _thisAssembly.GetName();
        _moduleAssembly = LoadFromAssemblyPath(mainModulePathAssemblyPath);
        _nativeProbeDirs = BuildNativeProbeDirs(_assemblyDir);
    }

    protected override Assembly? Load(AssemblyName assemblyName) {
        if (AssemblyName.ReferenceMatchesDefinition(_thisAssemblyName, assemblyName)) {
            return _thisAssembly;
        }

        foreach (Assembly loadedAssembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (!AssemblyName.ReferenceMatchesDefinition(loadedAssembly.GetName(), assemblyName)) {
                continue;
            }

            AssemblyLoadContext? loadContext = GetLoadContext(loadedAssembly);
            if (ReferenceEquals(loadContext, Default)) {
                return loadedAssembly;
            }
        }

        string asmPath = Path.Join(_assemblyDir, $"{assemblyName.Name}.dll");
        return File.Exists(asmPath) ? LoadFromAssemblyPath(asmPath) : null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName) {
        foreach (string candidateName in GetNativeLibraryFileNames(unmanagedDllName)) {
            foreach (string probeDir in _nativeProbeDirs) {
                string candidatePath = Path.Combine(probeDir, candidateName);
                if (!File.Exists(candidatePath)) {
                    continue;
                }

                return LoadUnmanagedDllFromPath(candidatePath);
            }
        }

        return IntPtr.Zero;
    }

    private static string[] GetNativeLibraryFileNames(string unmanagedDllName) {
        return Path.HasExtension(unmanagedDllName)
            ? [unmanagedDllName]
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? [$"{unmanagedDllName}.dll"]
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? [$"lib{unmanagedDllName}.dylib", $"{unmanagedDllName}.dylib"]
            : [$"lib{unmanagedDllName}.so", $"{unmanagedDllName}.so"];
    }

    private static string[] BuildNativeProbeDirs(string assemblyDir) {
        List<string> dirs = [assemblyDir];

        foreach (string ridDir in GetPreferredRidDirectories()) {
            string candidate = Path.Combine(assemblyDir, ridDir);
            if (Directory.Exists(candidate)) {
                dirs.Add(candidate);
            }
        }

        return [.. dirs];
    }

    private static string[] GetPreferredRidDirectories() {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? RuntimeInformation.ProcessArchitecture switch {
                Architecture.X64 => ["win-x64"],
                Architecture.Arm64 => ["win-arm64", "win-x64"],
                Architecture.X86 => ["win-x86", "win-x64"],
                Architecture.Arm => ["win-arm"],
                _ => ["win-x64"]
            }
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? RuntimeInformation.ProcessArchitecture switch {
                Architecture.Arm64 => ["osx-arm64"],
                Architecture.X64 => ["osx-x64", "osx-arm64"],
                _ => ["osx-arm64"]
            }
            : RuntimeInformation.ProcessArchitecture switch {
                Architecture.Arm64 => ["linux-arm64", "linux-x64"],
                Architecture.X64 => ["linux-x64", "linux-arm64"],
                _ => ["linux-x64"]
            };
    }

    public static Assembly Initialize() {
        LoadContext? instance = _instance;
        if (instance is not null) {
            return instance._moduleAssembly;
        }

        lock (_sync) {
            if (_instance is not null) {
                return _instance._moduleAssembly;
            }

            string assemblyPath = typeof(LoadContext).Assembly.Location;
            string assemblyDir = Path.GetDirectoryName(assemblyPath)
                ?? throw new InvalidOperationException("Unable to determine PSTextMate.ALC assembly directory.");
            string assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            const string AlcSuffix = ".ALC";
            if (!assemblyName.EndsWith(AlcSuffix, StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Unexpected ALC assembly name '{assemblyName}'.");
            }

            string moduleName = assemblyName[..^AlcSuffix.Length];
            string modulePath = Path.Combine(assemblyDir, $"{moduleName}.dll");
            if (!File.Exists(modulePath)) {
                throw new FileNotFoundException($"Could not load file or assembly '{modulePath}'. The system cannot find the file specified.", modulePath);
            }

            _instance = new LoadContext(modulePath);
            return _instance._moduleAssembly;
        }
    }
}
#endif
