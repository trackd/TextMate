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
    It 'Formats a PSObject with PSChildName and returns rendered output containing the filename' {
        $out2 = $psowrapped | Show-TextMate
        $out2 | Should -Not -BeNullOrEmpty
        $rendered2 = _GetSpectreRenderable @($out2) -EscapeAnsi
        $rendered2 | Should -Match 'FooBar|Foo-Bar'
    }
    It "Can render markdown" {
        $file = Get-Item -Path (Join-Path $PSScriptRoot 'test-markdown.md')
        $out = $file | Show-TextMate
        $out | Should -Not -BeNullOrEmpty
        $rendered = _GetSpectreRenderable $out -EscapeAnsi
        $rendered | Should -Match '# Markdown Rendering Test File'
    }
}
