BeforeAll {
    if (-not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }
}

Describe 'Format-PowerShell' -Tag 'Adapter' {
    It 'Formats a simple PowerShell string and returns renderables' {
        $ps = 'function Test-Thing { Write-Output "hi" }'
        $out = $ps | Format-PowerShell -OutputFormat Spectre
        $out | Should -Not -BeNullOrEmpty
        $out.Language | Should -Be 'powershell'
        $out.LineCount | Should -BeGreaterThan 0
        $out.ShowLineNumbers | Should -Be $false
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'function|Write-Output'
    }

    It 'Formats a simple PowerShell string' {
        $ps = 'function Test-Thing { Write-Output "hi" }'
        $out = $ps | Format-PowerShell -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'function|Write-Output'
    }

    It 'Formats a PowerShell file and returns renderables' {
        $filename = Join-Path $PSScriptRoot ('{0}.ps1' -f (Get-Random))
        'function Temp { Write-Output "ok" }' | Set-Content -Path $filename
        try {
            $out = Get-Item $filename | Format-PowerShell -OutputFormat Spectre
            $out | Should -Not -BeNullOrEmpty
            $out.Language | Should -Be 'powershell'
            $out.LineCount | Should -BeGreaterThan 0
            $renderedFile = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
            $renderedFile | Should -Match 'function|Write-Output'
        } finally {
            Remove-Item -Force -ErrorAction SilentlyContinue $filename
        }
    }

    It 'Renders a comment-only script without crashing' {
        $ps = "# This is a comment`n# Another comment"
        $out = $ps | Format-PowerShell -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'comment'
    }

    It 'Splits multiline pipeline items into individual lines' {
        $s1 = "# a`nfunction A { }"
        $s2 = "# b`nfunction B { }"
        $out = @($s1, $s2) | Format-PowerShell -OutputFormat Spectre
        $out.LineCount | Should -Be 4
    }

    It 'Preserves content of a here-string assignment' {
        $ps = '@"`nHello World`n"@'
        $out = $ps | Format-PowerShell -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Hello World'
    }

    It 'Renders pipeline operators and cmdlet names' {
        $ps = 'Get-Process | Where-Object { $_.CPU -gt 10 } | Sort-Object CPU'
        $out = $ps | Format-PowerShell -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Get-Process'
        $rendered | Should -Match 'Where-Object'
        $rendered | Should -Match 'Sort-Object'
    }

    It 'Preserves variable names in output' {
        $ps = '$myVar = 42; $anotherVar = "hello"'
        $out = $ps | Format-PowerShell -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'myVar'
        $rendered | Should -Match 'anotherVar'
    }

    It 'Renders try-catch block structure' {
        $ps = "try { Get-Item 'x' } catch { Write-Error `$_ }"
        $out = $ps | Format-PowerShell -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'try'
        $rendered | Should -Match 'catch'
    }

    It 'Preserves unicode characters inside a string literal' {
        $ps = '"emoji: 🚀 kanji: 日本語"'
        $out = $ps | Format-PowerShell -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match '🚀'
        $rendered | Should -Match '日本語'
    }

    It 'Should have Help and examples' {
        $help = Get-Help Format-PowerShell -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
        $help.examples.example.Count | Should -BeGreaterThan 1
    }
}
