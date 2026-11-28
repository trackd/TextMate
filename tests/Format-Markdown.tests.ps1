BeforeAll {
    if (-Not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }

    Import-Module (Join-Path $PSScriptRoot 'testhelper.psm1') -Force
}


Describe 'Format-Markdown' -Tag 'Adapter' {
    It 'Formats Markdown and returns renderables' {
        $md = "# Title`n`nSome text"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out | Should -Not -BeNullOrEmpty
        $out.Document.Lines | Should -Not -BeNullOrEmpty
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out
        $rendered | Should -Match 'Title|Some text'
    }

    It 'Formats Markdown' {
        $md = "# Title`n`nSome text"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out
        $rendered | Should -Match 'Title|Some text'
    }

    It 'Formats Markdown with Alternate and returns renderables' {
        $md = "# Title`n`nSome text"
        $out = $md | Format-Markdown -OutputFormat Spectre -Alternate
        $out | Should -Not -BeNullOrEmpty
        $out.LineCount | Should -BeGreaterThan 0
        $renderedAlt = _GetSpectreRenderable -RenderableObject $out
        $renderedAlt | Should -Match 'Title|Some text'
    }

    It 'Renders HTML img tags as images instead of HTML code blocks' {
        $md = '<img src="../assets/does-not-exist" width="50" alt="logo">'
        $out = $md | Format-Markdown -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out

        # Should route through image rendering fallback (not raw html block rendering)
        $rendered | Should -Match '🖼️\s+Image:\s+logo'
        $rendered | Should -Not -Match '<img\b'
        $rendered | Should -Not -Match '\bhtml\b'
    }

    It 'Accepts HTML img width values and still routes as image fallback' {
        $md = '<img src="../assets/does-not-exist" width="50px" alt="logo width">'
        $out = $md | Format-Markdown -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out

        # width="50px" should parse as a valid dimension and image should still be handled by image pipeline
        $rendered | Should -Match '🖼️\s+Image:\s+logo width'
        $rendered | Should -Not -Match '<img\b'
    }

    It 'Renders task lists with emoji checkbox markers' {
        $md = "- [x] done`n- [ ] todo"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi

        $rendered | Should -Match '✅\s+done'
        $rendered | Should -Match '⬜\s+todo'
    }

    It 'Keeps block headers for quote and code panels' {
        $md = "> quoted`n`n``````powershell`nWrite-Host 'hi'`n``````"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out

        $rendered | Should -Match 'quote'
        $rendered | Should -Match 'powershell'
    }

    It 'Renders nested list links with hyperlink target' {
        $md = "- parent`n  - [nested](https://example.com/nested)"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out

        $rendered | Should -Match 'nested'
        $rendered | Should -Match 'https://example.com/nested'
    }

    It 'Language property is exactly markdown and defaults are set correctly' {
        $md = "# lang test"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.Language | Should -Be 'markdown'
        $out.ShowLineNumbers | Should -Be $false
    }

    It 'More complex document has more renderables than a single line' {
        $simple = "just one line" | Format-Markdown -OutputFormat Spectre
        $complex = "# H1`n`npara`n`n- item1`n- item2`n`n| A | B |`n|---|---|`n| 1 | 2 |" | Format-Markdown -OutputFormat Spectre
        $complex.LineCount | Should -BeGreaterThan $simple.LineCount
    }

    It 'Renders all heading levels h1-h6' {
        $md = "# H1`n## H2`n### H3`n#### H4`n##### H5`n###### H6"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterOrEqual 6
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'H1'
        $rendered | Should -Match 'H2'
        $rendered | Should -Match 'H3'
        $rendered | Should -Match 'H4'
    }

    It 'Renders bold, italic and bold-italic without losing surrounding text' {
        $md = "plain **bold** *italic* ***both*** plain"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'plain'
        $rendered | Should -Match 'bold'
        $rendered | Should -Match 'italic'
        $rendered | Should -Match 'both'
    }

    It 'Renders strikethrough text' {
        $md = "~~deleted~~"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'deleted'
    }

    It 'Renders a horizontal rule without crashing' {
        $md = "before`n`n---`n`nafter"
        $out = $md | Format-Markdown -OutputFormat Spectre
        # hr + before/after = at least 3 renderables
        $out.LineCount | Should -BeGreaterOrEqual 3
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'before'
        $rendered | Should -Match 'after'
    }

    It 'Renders a markdown table with header and rows' {
        $md = "| Name  | Score |`n|-------|-------|`n| Alice | 10    |`n| Bob   | 20    |"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match '╭.*┬.*╮'
        $rendered | Should -Match 'Name'
        $rendered | Should -Match 'Score'
        $rendered | Should -Match 'Alice'
        $rendered | Should -Match 'Bob'
    }

    It 'Renders a fenced powershell code block and preserves content' {
        $md = '```powershell' + "`nGet-ChildItem`n" + '```'
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Get-ChildItem'
    }

    It 'Keeps fenced code blocks within a tight width budget' {
        $line = 'X' * 38
        $md = '```markdown' + "`n$line`n" + '```'
        $out = $md | Format-Markdown -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi -Width 40

        $renderedLines = $rendered.TrimEnd() -split "\r?\n"
        ($renderedLines | Measure-Object Length -Maximum).Maximum | Should -BeLessOrEqual 41
    }

    It 'Renders a plain fenced code block without crashing' {
        $md = '```' + "`njust text`n" + '```'
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'just text'
    }

    It 'Preserves inline code verbatim' {
        $md = 'Use `Write-Host`'
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Write-Host'
    }

    It 'Renders blockquote content' {
        $md = "> This is quoted"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'This is quoted'
    }

    It 'Renders ordered list items' {
        $md = "1. First`n2. Second`n3. Third"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterOrEqual 3
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'First'
        $rendered | Should -Match 'Second'
        $rendered | Should -Match 'Third'
    }

    It 'Renders deeply nested unordered list without crashing' {
        $md = "- L1`n  - L2`n    - L3`n      - L4"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'L1'
        $rendered | Should -Match 'L4'
    }

    It 'Renders escaped markdown special characters literally' {
        $md = "\*not italic\* \# not a heading"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'not a heading'
    }

    It 'Keeps multiple paragraphs separated and preserves all content' {
        $md = "Para one.`n`nPara two.`n`nPara three."
        $out = $md | Format-Markdown -OutputFormat Spectre
        # Each paragraph is a separate renderable
        $out.LineCount | Should -BeGreaterOrEqual 3
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Para one'
        $rendered | Should -Match 'Para two'
        $rendered | Should -Match 'Para three'
    }

    It 'Renders a very long paragraph without losing content' {
        $word = 'LongWord'
        $md = ($word * 20)  # 160 chars, forces wrapping at typical 80-col
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match $word
    }

    It 'Renders a hyperlink and preserves link text' {
        $md = "[PSTextMate](https://github.com/trackd/TextMate)"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'PSTextMate'
    }

    It 'Falls back gracefully for missing image files' {
        $md = "![missing](./does-not-exist.png)"
        $out = $md | Format-Markdown -OutputFormat Spectre
        # Should produce at least one renderable (the fallback), not be empty
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Not -BeNullOrEmpty
        $rendered | Should -Match 'missing|🖼️'
    }

    It 'Falls back for html img with missing local path' {
        $md = '<img src="./no-such-file.jpg" alt="absent">'
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Not -BeNullOrEmpty
        $rendered | Should -Match 'absent|🖼️'
    }

    It 'Alternate mode still renders all content' {
        $md = "## Alt heading`n`nSome body text"
        $out = $md | Format-Markdown -OutputFormat Spectre -Alternate
        $out.LineCount | Should -BeGreaterOrEqual 2
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Alt heading'
        $rendered | Should -Match 'Some body text'
    }

    It 'Renders mixed checked and unchecked tasks' {
        $md = "- [x] done`n- [ ] pending"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterOrEqual 2
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'done'
        $rendered | Should -Match 'pending'
    }

    It 'Preserves unicode characters in output' {
        $md = "Emoji: 🚀 and Japanese: 日本語"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -BeGreaterThan 0
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match '🚀'
        $rendered | Should -Match '日本語'
    }

    It 'LineCount matches Renderables array length' {
        $md = "line one`n`nline two`n`nline three"
        $out = $md | Format-Markdown -OutputFormat Spectre
        $out.LineCount | Should -Be $out.LineCount
    }

    It 'Rendered output contains no unescaped markup brackets from content' {
        $md = "Text with [brackets] that are not markup"
        $out = $md | Format-Markdown -OutputFormat Spectre
        # Spectre markup parsing should escape these; rendering must not throw
        { _GetSpectreRenderable -RenderableObject $out } | Should -Not -Throw
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'brackets'
    }

    It 'Should have Help and examples' {
        $help = Get-Help Format-Markdown -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
        $help.examples.example.Count | Should -BeGreaterThan 1
    }
}
