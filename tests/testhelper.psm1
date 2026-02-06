function _GetSpectreRenderable {
    param(
        [Parameter(Mandatory)]
        [object] $RenderableObject,
        [switch] $EscapeAnsi
    )
    try {
        [Spectre.Console.Rendering.Renderable]$RenderableObject = $RenderableObject
        $writer = [System.IO.StringWriter]::new()
        $output = [Spectre.Console.AnsiConsoleOutput]::new($writer)
        $settings = [Spectre.Console.AnsiConsoleSettings]::new()
        $settings.Out = $output
        $console = [Spectre.Console.AnsiConsole]::Create($settings)
        $console.Write($RenderableObject)
        if ($EscapeAnsi) {
            return $writer.ToString() | _EscapeAnsi
        }
        $writer.ToString()
    }
    finally {
        $writer.Dispose()
    }
}
filter _EscapeAnsi {
    [System.Management.Automation.Host.PSHostUserInterface]::GetOutputString($_, $false)
}

Export-ModuleMember -Function _GetSpectreRenderable, _EscapeAnsi
