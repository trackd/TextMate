BeforeAll {
    if (-not (Get-Module 'PSTextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'PSTextMate.psd1') -ErrorAction Stop
    }
}

Describe 'Format-PowerShell' {
    It 'Formats a simple PowerShell string and returns renderables' {
        $ps = 'function Test-Thing { Write-Output "hi" }'
        $out = $ps | Format-PowerShell
        $out | Should -Not -BeNullOrEmpty
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'function|Write-Output'
    }

    It 'Formats a simple PowerShell string' {
        $ps = 'function Test-Thing { Write-Output "hi" }'
        $out = $ps | Format-PowerShell
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'function|Write-Output'
    }

    It 'Formats a PowerShell file and returns renderables' {
        $filename = Join-Path $PSScriptRoot ('{0}.ps1' -f (Get-Random))
        'function Temp { Write-Output "ok" }' | Set-Content -Path $filename
        try {
            $out = Get-Item $filename | Format-PowerShell
            $out | Should -Not -BeNullOrEmpty
            $renderedFile = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
            $renderedFile | Should -Match 'function|Write-Output'
        } finally {
            Remove-Item -Force -ErrorAction SilentlyContinue $filename
        }
    }
}
