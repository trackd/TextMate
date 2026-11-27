<#
.SYNOPSIS
Shows shared TextMate panels, styles, columns and progress output.
.DESCRIPTION
Imports the local build and composes one layout that can be returned as built-in,
Spectre, Fullscreen or HTML output. Optional frameworks load only when requested.
.PARAMETER Width
The initial layout width in terminal cells. Native containers can resize it later.
.PARAMETER OutputFormat
The output representation. Text requires no UI framework.
.EXAMPLE
./examples/Show-TextMateComposition.ps1 -Width 100
.EXAMPLE
./examples/Show-TextMateComposition.ps1 -OutputFormat Html | Set-Content ./composition.html
.OUTPUTS
PSTextMate.Core.HighlightedDocument, a native renderable, or an HTML string.
.NOTES
Build the module with ./build.ps1 before running this repository example.
#>
using namespace PSTextMate.Core

param(
    [ValidateRange(8, 1000)]
    [int]$Width = 80,

    [ValidateSet('Text', 'Spectre', 'Fullscreen', 'Html')]
    [string]$OutputFormat = 'Text'
)

Import-Module (Join-Path $PSScriptRoot '../output/TextMate.psd1') -ErrorAction Stop

$heading = [TextStyle]::Plain.WithForeground([TextColor]::Parse('#66D9EF')).WithDecoration('Bold')
$muted = [TextStyle]::Plain.WithForeground([TextColor]::Gray)
$code = @'
$items = Get-Process
$items | Sort-Object CPU -Descending |
    Select-Object -First 5 Name, CPU
'@ | Format-PowerShell
$codePanel = $code.AsContent().LineNumbers().Panel('PowerShell', 'Shared highlighting')

$options = [PanelOptions]@{
    Header = 'Status'
    Footer = '7 of 10 complete'
    BorderStyle = $muted
    TitleStyle = $heading
}
$statusPanel = [TextContent]::Rows(
    [TextContent]::Text('Ready to compose', $heading),
    [TextContent]::Text('One layout for every backend'),
    [TextContent]::Rule('Progress', $muted),
    [TextContent]::Progress(7, 10),
    [TextContent]::Text('70%').Align('Right')
).Panel($options)

$content = [TextContent]::Rows(
    [TextContent]::Rule('TextMate composition', $heading),
    [TextContent]::Columns(2, [TextColumn]::new($codePanel), [TextColumn]::new($statusPanel)),
    [TextContent]::Text('Panels · colors · numbered code · responsive columns', $muted).Align('Center')
)
$content.ConvertTo($OutputFormat, $Width)
