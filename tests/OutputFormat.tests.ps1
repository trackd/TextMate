BeforeAll {
    if (-not (Get-Module TextMate)) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }
}

Describe 'Output format presentation parity' -Tag 'Adapter' {
    It 'Exports shared Markdown as escaped HTML without changing its presentation' {
        $source = "# Heading`n`n**bold** and [safe](https://example.com) and [unsafe](javascript:alert).`n`n``<script>``"
        $document = $source | Format-Markdown -Width 40
        $html = $document.ConvertTo('Html')
        $html | Should -BeOfType ([string])
        $html | Should -Match 'font-weight:bold'
        $html | Should -Match 'href="https://example.com"'
        $html | Should -Not -Match 'href="javascript:'
        $html | Should -Not -Match '<script>'
        $plain = [System.Net.WebUtility]::HtmlDecode(($html -replace '<[^>]*>', ''))
        $plain | Should -BeExactly $document.Text
        ($source | Format-Markdown -Width 40 -OutputFormat Html) | Should -BeExactly $html
    }

    It 'Converts the built-in object to both native output types' {
        $document = 'Write-Output 42' | Format-PowerShell
        $document.ConvertTo('Spectre').GetType().FullName | Should -BeExactly 'PSTextMate.Spectre.TextMateRenderable'
        $document.ConvertTo('Fullscreen').GetType().FullName | Should -BeExactly 'PSTextMate.XenoAtom.TextMateVisual'
        $document.ToSpectre().GetType().FullName | Should -BeExactly 'PSTextMate.Spectre.TextMateRenderable'
        $document.ToFullscreen().GetType().FullName | Should -BeExactly 'PSTextMate.XenoAtom.TextMateVisual'
        $document.ToDocument().Text | Should -BeExactly $document.Text
        $content = $document.AsContent().Panel('Code')
        $content.ToSpectre(40).Document.Text | Should -BeExactly $content.ToDocument(40).Text
        $content.ToFullscreen(40).Document.Text | Should -BeExactly $content.ToDocument(40).Text
        $content.ToHtml($true, 40) | Should -BeExactly $content.ToDocument(40).ToHtml($true)
        $expected = $document | Out-String -Width 120
        foreach ($format in @('Spectre', 'Fullscreen')) {
            ($document.ConvertTo($format) | Out-String -Width 120) | Should -BeExactly $expected
        }
    }



    It 'Displays identical Sixel module image cells in every output format' {
        $check = {
            param($moduleFile)
            $ErrorActionPreference = 'Stop'
            New-Module -Name Sixel -ScriptBlock {
                function ConvertTo-Sixel {
                    param($Path, $Width, $Height, $Protocol, [switch]$Force)
                    "`e[38;2;255;0;0;48;2;0;0;255m▀`e[0m"
                }
                Export-ModuleMember -Function ConvertTo-Sixel
            } | Import-Module -Global
            Import-Module $moduleFile
            $results = foreach ($format in @('Text', 'Spectre', 'Fullscreen')) {
                $value = '![red](red.bmp)' | Format-Markdown -OutputFormat $format -Width 40
                [pscustomobject]@{
                    Type = $value.GetType().FullName
                    Display = $value | Out-String -Width 120
                }
            }
            $results | ConvertTo-Json -Compress
        }
        $moduleFile = Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1'
        $results = (& (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        $results[0].Display | Should -Match '▀'
        $results[1].Display | Should -BeExactly $results[0].Display
        $results[2].Display | Should -BeExactly $results[0].Display
        $results.Type | Should -Be @(
            'PSTextMate.Core.HighlightedDocument'
            'PSTextMate.Spectre.TextMateRenderable'
            'PSTextMate.XenoAtom.TextMateVisual'
        )
    }

    It 'Displays identical rich Markdown in PowerShell while retaining native types' {
        $source = "# Heading`n`n**bold** and [link](https://example.com).`n`n| A | B |`n|---|---|`n| 1 | 2 |"
        $renderedText = $source | Format-Markdown -Width 40 -ErrorAction Stop
        $expected = $renderedText | Out-String -Width 120
        $expected | Should -Not -Match '\*\*|\]\('
        $expected | Should -Match 'Heading'
        foreach ($case in @(
            @{ Format = 'Spectre'; Type = 'PSTextMate.Spectre.TextMateRenderable' }
            @{ Format = 'Fullscreen'; Type = 'PSTextMate.XenoAtom.TextMateVisual' }
        )) {
            $native = $source | Format-Markdown -Width 40 -OutputFormat $case.Format -ErrorAction Stop
            $native.GetType().FullName | Should -Be $case.Type
            ($native | Out-String -Width 120) | Should -BeExactly $expected
        }
    }

    It 'Pages native output through the same document as built-in output' {
        $moduleFile = Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1'
        $check = {
            param($moduleFile, $format)
            $ErrorActionPreference = 'Stop'
            Import-Module $moduleFile
            "# Heading`n`n**bold**" | Format-Markdown -Width 40 -OutputFormat $format -ErrorAction Stop | Out-Page
        }
        $expected = & (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile, 'Text'
        $LASTEXITCODE | Should -Be 0
        foreach ($format in @('Spectre', 'Fullscreen')) {
            $actual = & (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile, $format
            $LASTEXITCODE | Should -Be 0
            ($actual -join "`n") | Should -BeExactly ($expected -join "`n")
        }
    }
}
