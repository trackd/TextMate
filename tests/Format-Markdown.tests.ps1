BeforeAll {
    if (-not (Get-Module 'PSTextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'PSTextMate.psd1') -ErrorAction Stop
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
}
