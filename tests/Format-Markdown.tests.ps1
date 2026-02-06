Describe 'Format-Markdown' {
    It 'Formats Markdown and returns renderables' {
        $md = "# Title\n\nSome text"
        $out = $md | Format-Markdown
        $out | Should -Not -BeNullOrEmpty
        $rendered =  _GetSpectreRenderable -RenderableObject $out
        $rendered | Should -Match '# Title|Title|Some text'
    }

    It 'Outputs every single line when -Lines is used' {
        $md = "# Title\n\nSome text"
        $Lines = $md | Format-Markdown -Lines
        $Lines | Should -BeOfType Spectre.Console.Paragraph
        $rendered = _GetSpectreRenderable -RenderableObject $Lines
        $rendered = $Lines | ForEach-Object { _GetSpectreRenderable -RenderableObject $_ } | Out-String
        $rendered | Should -Match '# Title|Title|Some text'
    }

    It 'Formats Markdown with Alternate and returns renderables' {
        $md = "# Title\n\nSome text"
        $out = $md | Format-Markdown -Alternate
        $out | Should -Not -BeNullOrEmpty
        $renderedAlt =  _GetSpectreRenderable -RenderableObject $out
        $renderedAlt | Should -Match '# Title|Title|Some text'
    }
}
