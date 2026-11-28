using System.Reflection;

namespace PSTextMate.Utilities;

internal static class PipelineControl {
    private static readonly ConstructorInfo? StopUpstream = FindStopUpstream();

    /// <summary>
    /// The exception Select-Object -First throws to end the commands before it. Unlike PipelineStoppedException,
    /// it ends only the current pipeline, so the script that contains it continues. Null if the engine lacks it.
    /// </summary>
    internal static Exception? CreateStopUpstream(PSCmdlet cmdlet) {
        ArgumentNullException.ThrowIfNull(cmdlet);
        return StopUpstream?.Invoke([cmdlet]) as Exception;
    }

    private static ConstructorInfo? FindStopUpstream() {
        Assembly engine = typeof(PSCmdlet).Assembly;
        Type? command = engine.GetType("System.Management.Automation.Internal.InternalCommand");
        return command is null ? null : engine.GetType("System.Management.Automation.StopUpstreamCommandsException")
            ?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [command]);
    }
}
