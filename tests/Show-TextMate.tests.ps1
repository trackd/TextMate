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
        $rendered = _GetSpectreRenderable -RenderableObject $out2 -EscapeAnsi
        $rendered | Should -Match 'FooBar|Foo-Bar'
    }
    It 'Outputs every single line when -Lines is used' {
        $out = $psString | Show-TextMate -Lines
        $out | Should -BeOfType Spectre.Console.Paragraph
        $rendered = $out | ForEach-Object { _GetSpectreRenderable -RenderableObject $_ -EscapeAnsi } | Out-String
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
