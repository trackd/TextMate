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



function Get-HostBuffer {
    <#
    Applications that use the GetConsoleScreenBufferInfo family of APIs to retrieve the active console colors in Win32 format and then attempt to transform them into cross-platform VT sequences (for example, by transforming BACKGROUND_RED to \x1b[41m) may interfere with Terminal's ability to detect what background color the application is attempting to use.

    Application developers are encouraged to choose either Windows API functions or VT sequences for adjusting colors and not attempt to mix them.
    https://learn.microsoft.com/en-us/windows/terminal/troubleshooting#technical-notes
    https://learn.microsoft.com/en-us/windows/console/getconsolescreenbufferinfoex
    #>
    $windowSize = $host.UI.RawUI.WindowSize
    $windowPosition = $host.UI.RawUI.WindowPosition
    $windowWidth = $windowSize.Width
    $windowHeight = $windowSize.Height
    $windowRect = [System.Management.Automation.Host.Rectangle]::new(
        $windowPosition.X,
        $windowPosition.Y,
        ($windowPosition.X + $windowWidth - 1),
        ($windowPosition.Y + $windowHeight - 1))
    $windowBuffer = $host.UI.RawUI.GetBufferContents($windowRect)
    foreach ($x in 0..($windowHeight - 1)) {
        $row = foreach ($y in 0..($windowWidth - 1)) {
            $windowBuffer[$x, $y].Character
        }
        -join $row
    }
}

Export-ModuleMember -Function _GetSpectreRenderable, Get-HostBuffer
