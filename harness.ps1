#!/usr/bin/env pwsh
param([switch]$Load)
$s = {
    param([string]$Path, [switch]$LoadOnly)
    Import-Module (Join-Path $Path 'output' 'TextMate.psd1')
    if (-not $LoadOnly) {
        Format-Markdown (Join-Path $Path 'tests' 'test-markdown.md')
    }
}
if ($Load) {
    . $s -LoadOnly -Path $PSScriptRoot
} else {
    pwsh -nop -c $s -args $PSScriptRoot
}
