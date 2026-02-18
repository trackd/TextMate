using namespace System.Management.Automation;
using namespace Spectre.Console;
using namespace Spectre.Console.Rendering;

function _GetSpectreRenderable {
    param(
        [Parameter(Mandatory)]
        [Renderable] $RenderableObject,
        [switch] $EscapeAnsi
    )
    try {
        $writer = [System.IO.StringWriter]::new()
        $output = [AnsiConsoleOutput]::new($writer)
        $settings = [AnsiConsoleSettings]::new()
        $settings.Out = $output
        $console = [AnsiConsole]::Create($settings)
        $console.Write($RenderableObject)
        if ($EscapeAnsi) {
            return $writer.ToString() | _EscapeAnsi
        }
        $writer.ToString()
    }
    finally {
        ${writer}?.Dispose()
    }
}
filter _EscapeAnsi {
    [Host.PSHostUserInterface]::GetOutputString($_, $false)
}

Export-ModuleMember -Function _GetSpectreRenderable, _EscapeAnsi
