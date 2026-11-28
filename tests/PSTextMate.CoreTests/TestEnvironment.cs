using System.Runtime.CompilerServices;
using PSTextMate.Core;

namespace PSTextMate.CoreTests;

internal static class TestEnvironment {
    /// <summary>
    /// Output assertions expect 24-bit color. Detection follows the environment, and CI runners on Linux and
    /// macOS set no COLORTERM, so pin the depth instead of inheriting the runner's.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize() => VT.ColorDepth = TerminalColorDepth.TrueColor;
}
