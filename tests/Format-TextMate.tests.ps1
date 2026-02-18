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
        $out2 = $psowrapped | Format-TextMate
        $out2 | Should -Not -BeNullOrEmpty
        $rendered = _GetSpectreRenderable -RenderableObject $out2 -EscapeAnsi
        $rendered | Should -Match 'FooBar|Foo-Bar'
    }
    It 'Formats a simple PowerShell string' {
        $out = $psString | Format-TextMate
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'function|Write-Host|Foo-Bar'
    }
    It "Can render markdown" {
        $file = Get-Item -Path (Join-Path $PSScriptRoot 'test-markdown.md')
        $out = $file | Format-TextMate
        $out | Should -Not -BeNullOrEmpty
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Markdown Test File'
        $rendered | Should -Match 'Path.GetExtension'
    }
    It 'Should have Help and examples' {
        $help = Get-Help Format-TextMate -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
        $help.examples.example.Count | Should -BeGreaterThan 1
    }
}
