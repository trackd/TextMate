BeforeAll {
    if (-Not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }
}


Describe 'Format-Markdown' {
    It 'Formats Markdown and returns renderables' {
        $md = "# Title\n\nSome text"
        $out = $md | Format-Markdown
        $out | Should -Not -BeNullOrEmpty
        $rendered =  _GetSpectreRenderable -RenderableObject $out
        $rendered | Should -Match '# Title|Title|Some text'
    }

    It 'Formats Markdown' {
        $md = "# Title\n\nSome text"
        $out = $md | Format-Markdown
        $rendered = _GetSpectreRenderable -RenderableObject $out
        $rendered | Should -Match '# Title|Title|Some text'
    }

    It 'Formats Markdown with Alternate and returns renderables' {
        $md = "# Title\n\nSome text"
        $out = $md | Format-Markdown -Alternate
        $out | Should -Not -BeNullOrEmpty
        $renderedAlt =  _GetSpectreRenderable -RenderableObject $out
        $renderedAlt | Should -Match '# Title|Title|Some text'
    }

    It 'Renders HTML img tags as images instead of HTML code blocks' {
        $md = '<img src="../assets/does-not-exist" width="50" alt="logo">'
        $out = $md | Format-Markdown
        $rendered = _GetSpectreRenderable -RenderableObject $out

        # Should route through image rendering fallback (not raw html block rendering)
        $rendered | Should -Match '🖼️\s+Image:\s+logo'
        $rendered | Should -Not -Match '<img\b'
        $rendered | Should -Not -Match '\bhtml\b'
    }

    It 'Accepts HTML img width values and still routes as image fallback' {
        $md = '<img src="../assets/does-not-exist" width="50px" alt="logo width">'
        $out = $md | Format-Markdown
        $rendered = _GetSpectreRenderable -RenderableObject $out

        # width="50px" should parse as a valid dimension and image should still be handled by image pipeline
        $rendered | Should -Match '🖼️\s+Image:\s+logo width'
        $rendered | Should -Not -Match '<img\b'
    }
    It 'Should have Help and examples' {
        $help = Get-Help Format-Markdown -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
        $help.examples.example.Count | Should -BeGreaterThan 1
    }
}
