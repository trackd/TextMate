BeforeAll {
    if (-not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }
    $psString = @'
function Foo-Bar {
    param([string]$Name)
        Write-Host "Hello, $Name!"
}
'@
    $psowrapped = [psobject]::new($psString)
    $note = [PSNoteProperty]::new('PSChildName', 'FooBar.ps1')
    $psowrapped.psobject.properties.add($note)
}

Describe 'Format-TextMate' {
    It 'Formats a PSObject with PSChildName and returns rendered PowerShell output' {
        $out2 = $psowrapped | Format-TextMate -OutputFormat Spectre
        $out2 | Should -Not -BeNullOrEmpty
        $out2.Document.Lines | Should -Not -BeNullOrEmpty
        $out2.Language | Should -Be 'powershell'
        $out2.ShowLineNumbers | Should -Be $false
        $rendered = _GetSpectreRenderable -RenderableObject $out2 -EscapeAnsi
        $rendered | Should -Match 'FooBar|Foo-Bar'
    }
    It 'Formats a simple PowerShell string' {
        $out = $psString | Format-TextMate -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'function|Write-Host|Foo-Bar'
    }
    It "Can render markdown" {
        $file = Get-Item -Path (Join-Path $PSScriptRoot 'test-markdown.md')
        $out = $file | Format-TextMate -OutputFormat Spectre
        $out | Should -Not -BeNullOrEmpty
        $out.LineCount | Should -BeGreaterThan 1
        $out.Language | Should -Be 'markdown'
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Markdown Test File'
        $rendered | Should -Match 'Path.GetExtension'
    }
    It 'Should have Help and examples' {
        $help = Get-Help Format-TextMate -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
        $help.examples.example.Count | Should -BeGreaterThan 1
    }

    It 'Honours -Language powershell override for string input' {
        $ps = 'function Get-Thing { param($x) $x }'
        $out = $ps | Format-TextMate -OutputFormat Spectre -Language 'powershell'
        $out.Language | Should -Be 'powershell'
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'function'
        $rendered | Should -Match 'Get-Thing'
    }

    It 'Honours -Language csharp override for string input' {
        $cs = 'public class Edge { public int Value { get; set; } }'
        $out = $cs | Format-TextMate -OutputFormat Spectre -Language 'csharp'
        $out.Language | Should -Be 'csharp'
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'public class Edge'
    }

    It 'Honours -Language markdown for a plain string' {
        $md = "# Auto-detected heading`n`nsome body"
        $out = $md | Format-TextMate -OutputFormat Spectre -Language 'markdown'
        $out.Language | Should -Be 'markdown'
        $out.LineCount | Should -BeGreaterOrEqual 2
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Auto-detected heading'
        $rendered | Should -Match 'some body'
    }

    It 'Auto-detects C# from a .cs file object' {
        $temp = Join-Path $PSScriptRoot 'temp_ftm.cs'
        'public class AutoDetect { }' | Out-File -FilePath $temp -Encoding utf8
        try {
            $out = Get-Item $temp | Format-TextMate -OutputFormat Spectre
            $out.Language | Should -Be 'csharp'
            $out.LineCount | Should -BeGreaterThan 0
            $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
            $rendered | Should -Match 'AutoDetect'
        } finally {
            Remove-Item -Force -ErrorAction SilentlyContinue $temp
        }
    }

    It 'Auto-detects Markdown from a .md file object' {
        $temp = Join-Path $PSScriptRoot 'temp_ftm.md'
        "# AutoMD`n`nbody text" | Out-File -FilePath $temp -Encoding utf8
        try {
            $out = Get-Item $temp | Format-TextMate -OutputFormat Spectre
            $out.Language | Should -Be 'markdown'
            $out.LineCount | Should -BeGreaterOrEqual 2
            $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
            $rendered | Should -Match 'AutoMD'
            $rendered | Should -Match 'body text'
        } finally {
            Remove-Item -Force -ErrorAction SilentlyContinue $temp
        }
    }

    It 'LineCount property matches number of piped lines' {
        $lines = "line one`nline two`nline three"
        $out = $lines | Format-TextMate -OutputFormat Spectre -Language 'powershell'
        $out.LineCount | Should -Be 3
        $out.LineCount | Should -Be 3
    }

    It 'Renders output without truncating content' {
        $ps = 'Write-Host "hello world"'
        $out = $ps | Format-TextMate -OutputFormat Spectre -Language 'powershell'
        $narrow = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $narrow | Should -Match 'Write-Host'
        $narrow | Should -Match 'hello world'
    }

    It 'Preserves unicode content in highlighted output' {
        $ps = '# comment with emoji 🎉 and kanji 漢字'
        $out = $ps | Format-TextMate -OutputFormat Spectre -Language 'powershell'
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match '🎉'
        $rendered | Should -Match '漢字'
    }

    It 'LineCount equals Renderables.Count for a fresh object' {
        $ps = "a`nb`nc`nd"
        $out = $ps | Format-TextMate -OutputFormat Spectre -Language 'powershell'
        $out.LineCount | Should -Be $out.LineCount
    }
}
