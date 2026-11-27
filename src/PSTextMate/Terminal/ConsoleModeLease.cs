using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PSTextMate.Core;

internal interface IConsoleModeApi {
    nint Handle(int kind);
    bool Get(nint handle, out uint mode);
    bool Set(nint handle, uint mode);
    int Error { get; }
}

/// <summary>Owns Windows console modes for the pager and restores the complete entry bitmasks.</summary>
internal sealed class ConsoleModeLease : IDisposable {
    private readonly IConsoleModeApi _api;
    private readonly nint _input;
    private readonly nint _output;
    private readonly uint _inputMode;
    private readonly uint _outputMode;
    private bool _restoreInput;
    private bool _restoreOutput;

    internal static ConsoleModeLease? Enter() => OperatingSystem.IsWindows() ? new(new WindowsConsoleModeApi()) : null;

    internal ConsoleModeLease(IConsoleModeApi api) {
        _api = api;
        _input = api.Handle(-10);
        _output = api.Handle(-11);
        if (_input is 0 or -1 || !api.Get(_input, out _inputMode))
            throw Failure("capture console input mode");
        bool hasOutput = _output is not (0 or -1) && api.Get(_output, out _outputMode);
        try {
            // Console.ReadKey consumes console key records, not Sixel's raw VT byte stream.
            // Disable processing/line/echo/quick-edit and VT translation; preserve all other flags.
            uint readKeyMode = (_inputMode | 0x0080u) & ~0x0247u;
            _restoreInput = true;
            if (!api.Set(_input, readKeyMode))
                throw Failure("acquire pager input mode");
            if (hasOutput) {
                _restoreOutput = true;
                if (!api.Set(_output, _outputMode | 0x0005u))
                    throw Failure("enable pager VT output");
            }
        }
        catch {
            Dispose();
            throw;
        }
    }

    public void Dispose() {
        IOException? error = null;
        if (_restoreOutput) {
            if (_api.Set(_output, _outputMode))
                _restoreOutput = false;
            else
                error = Failure("restore console output mode");
        }
        if (_restoreInput) {
            if (_api.Set(_input, _inputMode))
                _restoreInput = false;
            else
                error ??= Failure("restore console input mode");
        }
        if (error is not null)
            throw error;
    }

    private IOException Failure(string operation) => new($"Unable to {operation}.", new Win32Exception(_api.Error));
}

// GetStdHandle returns borrowed handles. This class never closes them.
internal sealed partial class WindowsConsoleModeApi : IConsoleModeApi {
    public nint Handle(int kind) => GetStdHandle(kind);
    public bool Get(nint handle, out uint mode) => GetConsoleMode(handle, out mode) != 0;
    public bool Set(nint handle, uint mode) => SetConsoleMode(handle, mode) != 0;
    public int Error => Marshal.GetLastPInvokeError();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int kind);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetConsoleMode(nint handle, out uint mode);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int SetConsoleMode(nint handle, uint mode);
}
