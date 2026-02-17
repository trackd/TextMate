#!/usr/bin/env pwsh
param([switch]$Load)
$s = {
    param([string]$Path, [switch]$LoadOnly)
    $Parent = Split-Path $Path -Parent
    Import-Module (Join-Path $Parent 'PwshSpectreConsole' 'output' 'PwshSpectreConsole.psd1')
    Import-Module (Join-Path $Path 'output' 'PSTextMate.psd1')
    if (-not $LoadOnly) {
        Format-Markdown (Join-Path $Path 'tests' 'test-markdown.md')
    }
}
if ($Load) {
    . $s -LoadOnly -Path $PSScriptRoot
} else {
    pwsh -nop -c $s -args $PSScriptRoot
}
