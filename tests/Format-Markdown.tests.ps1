Describe 'Format-Markdown' {
    It 'Formats Markdown and returns renderables' {
        $md = "# Title\n\nSome text"
        $out = $md | Format-Markdown
        $out | Should -Not -BeNullOrEmpty
        $rendered = _GetSpectreRenderable $out -EscapeAnsi
        $rendered | Should -Match '# Title|Title|Some text'
    }

    It 'Formats Markdown with Alternate and returns renderables' {
        $md = "# Title\n\nSome text"
        $out = $md | Format-Markdown -Alternate
        $out | Should -Not -BeNullOrEmpty
        $renderedAlt = _GetSpectreRenderable $out -EscapeAnsi
        $renderedAlt | Should -Match '# Title|Title|Some text'
    }
}
