Describe 'Format-PowerShell' {
    It 'Formats a simple PowerShell string and returns renderables' {
        $ps = 'function Test-Thing { Write-Output "hi" }'
        $out = $ps | Format-PowerShell
        $out | Should -Not -BeNullOrEmpty
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'function|Write-Output'
    }

    It 'Outputs every single line when -Lines is used' {
        $ps = 'function Test-Thing { Write-Output "hi" }'
        $lines = $ps | Format-PowerShell -Lines
        $lines | Should -BeOfType Spectre.Console.Paragraph
        $rendered = $lines | ForEach-Object { _GetSpectreRenderable -RenderableObject $_ -EscapeAnsi } | Out-String
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
