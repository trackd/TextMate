BeforeAll {
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

Describe 'Show-TextMate' {
    It 'Formats a PSObject with PSChildName and returns rendered PowerShell output' {
        $out2 = $psowrapped | Show-TextMate
        $out2 | Should -Not -BeNullOrEmpty
        $rendered = _GetSpectreRenderable -RenderableObject $out2 -EscapeAnsi
        $rendered | Should -Match 'FooBar|Foo-Bar'
    }
    It 'Formats a simple PowerShell string' {
        $out = $psString | Show-TextMate
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'function|Write-Host|Foo-Bar'
    }
    It "Can render markdown" {
        $file = Get-Item -Path (Join-Path $PSScriptRoot 'test-markdown.md')
        $out = $file | Show-TextMate
        $out | Should -Not -BeNullOrEmpty
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Markdown Test File'
        $rendered | Should -Match 'Path.GetExtension'
    }
}
