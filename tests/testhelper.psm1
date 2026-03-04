using namespace System.Management.Automation;

function _GetSpectreRenderable {
    param(
        [Parameter(Mandatory)]
        [object] $RenderableObject,
        [switch] $EscapeAnsi
    )
    [PSTextMate.Utilities.SpectreRenderBridge]::RenderToString(
        $RenderableObject,
        $EscapeAnsi.IsPresent
    )
}
filter _EscapeAnsi {
    [Host.PSHostUserInterface]::GetOutputString($_, $false)
}

Export-ModuleMember -Function _GetSpectreRenderable, _EscapeAnsi
