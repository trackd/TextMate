BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
}

Describe 'Runtime highlighting registration' {
    BeforeEach {
        $suffix = [Guid]::NewGuid().ToString('N')
        $languageName = "probe$suffix"
        $aliasName = "alias$suffix"
        $themeName = "Theme$suffix"
        $extension = ".probe$suffix"
        $grammarFile = Join-Path $TestDrive 'probe.tmLanguage.json'
        $themeFile = Join-Path $TestDrive 'probe-theme.json'
        $updatedThemeFile = Join-Path $TestDrive 'updated-theme.json'
        @{ scopeName = "source.$languageName"; patterns = @(@{ match = '\bprobe\b'; name = 'keyword.control.probe' }) } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $grammarFile
        @{
            name = $themeName
            colors = @{ 'editor.foreground' = '#AABBCC'; 'editor.background' = '#101112' }
            tokenColors = @(@{ scope = 'keyword.control.probe'; settings = @{ foreground = '#FF0000' } })
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $themeFile
        @{
            name = $themeName
            colors = @{ 'editor.foreground' = '#AABBCC'; 'editor.background' = '#202122' }
            tokenColors = @(@{ scope = 'keyword.control.probe'; settings = @{ foreground = '#00FF00' } })
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $updatedThemeFile
    }

    It 'Registers languages after highlighting and updates discovery aliases and extensions' {
        $null = 'Write-Output 1' | Format-PowerShell
        $registered = Add-TextMateLanguage -Path $grammarFile -Name $languageName -Alias $aliasName -Extension $extension -PassThru
        $registered.Id | Should -BeExactly $languageName
        Test-TextMate -Language $languageName | Should -BeTrue
        Test-TextMate -Language $aliasName | Should -BeTrue
        Test-TextMate -Extension $extension | Should -BeTrue
        (Get-TextMateGrammar).Id | Should -Contain $languageName
        ('probe' | Format-TextMate -Language $aliasName).Language | Should -BeExactly $languageName
        $sample = Join-Path $TestDrive "example$extension"
        Set-Content -LiteralPath $sample -Value 'probe'
        (Format-TextMate $sample).Language | Should -BeExactly $languageName
    }

    It 'Registers custom theme names and freezes styles in existing documents' {
        Add-TextMateLanguage -Path $grammarFile -Name $languageName
        Add-TextMateTheme -Path $themeFile -Name $themeName -PassThru | Should -BeExactly $themeName
        $old = 'probe' | Format-TextMate -Language $languageName -Theme $themeName
        $old.Theme | Should -BeExactly $themeName
        $old.Ranges.ToArray()[0].Style.Foreground | Should -Be 0xFF0000
        $before = $old.ToHtml()
        Add-TextMateTheme -Path $updatedThemeFile -Name $themeName -Force
        $new = 'probe' | Format-TextMate -Language $languageName -Theme $themeName
        $new.Ranges.ToArray()[0].Style.Foreground | Should -Be 0x00FF00
        $old.ToHtml() | Should -BeExactly $before
        $old.ToHtml() | Should -Match 'background-color:#101112'
        $new.ToHtml() | Should -Match 'background-color:#202122'
        [PSTextMate.Core.HighlightingExtensions]::ThemeNames | Should -Contain $themeName
    }

    It 'Rejects duplicate registrations unless Force is explicit' {
        Add-TextMateLanguage -Path $grammarFile -Name $languageName -Extension $extension
        Add-TextMateTheme -Path $themeFile -Name $themeName
        { Add-TextMateLanguage -Path $grammarFile -Name $languageName -ErrorAction Stop } | Should -Throw '*already registered*'
        { Add-TextMateTheme -Path $themeFile -Name $themeName -ErrorAction Stop } | Should -Throw '*already registered*'
        { Add-TextMateLanguage -Path $grammarFile -Name 'another-language' -Extension $extension -ErrorAction Stop } | Should -Throw '*already registered*'
        Add-TextMateLanguage -Path $grammarFile -Name $languageName -Alias $aliasName -Extension $extension -Force
        ('probe' | Format-TextMate -Language $languageName).Language | Should -BeExactly $languageName
    }

    It 'WhatIf validates inputs without publishing catalog changes' {
        $preview = "Preview$suffix"
        Add-TextMateTheme -Path $themeFile -Name $preview -WhatIf
        Add-TextMateLanguage -Path $grammarFile -Name $preview -WhatIf
        [PSTextMate.Core.HighlightingExtensions]::ThemeNames | Should -Not -Contain $preview
        Test-TextMate -Language $preview | Should -BeFalse
    }

    It 'Reports malformed and missing files without changing registered themes' {
        $bad = Join-Path $TestDrive 'invalid.json'
        Set-Content -LiteralPath $bad -Value '{ broken'
        { Add-TextMateTheme -Path $bad -Name "Bad$suffix" -ErrorAction Stop } | Should -Throw
        { Add-TextMateLanguage -Path $bad -Name "Bad$suffix" -ErrorAction Stop } | Should -Throw
        { Add-TextMateTheme -Path (Join-Path $TestDrive 'missing.json') -Name "Missing$suffix" -ErrorAction Stop } | Should -Throw
        [PSTextMate.Core.HighlightingExtensions]::ThemeNames | Should -Not -Contain "Bad$suffix"
        { 'text' | Format-TextMate -Language plaintext -Theme "Missing$suffix" -ErrorAction Stop } | Should -Throw '*No TextMate theme*'
    }

    It 'Completes newly registered names and exposes usable help for both cmdlets' {
        Add-TextMateLanguage -Path $grammarFile -Name $languageName
        Add-TextMateTheme -Path $themeFile -Name $themeName
        $themeCompletion = [Management.Automation.CommandCompletion]::CompleteInput("Format-PowerShell -Theme $themeName", ("Format-PowerShell -Theme $themeName").Length, $null)
        $themeCompletion.CompletionMatches.ListItemText | Should -Contain $themeName
        $languageCompletion = [Management.Automation.CommandCompletion]::CompleteInput("Format-TextMate -Language $languageName", ("Format-TextMate -Language $languageName").Length, $null)
        $languageCompletion.CompletionMatches.ListItemText | Should -Contain $languageName
        foreach ($command in @('Add-TextMateLanguage', 'Add-TextMateTheme')) {
            $help = Get-Help $command -Full
            $help.Synopsis | Should -Not -BeNullOrEmpty
            @($help.Examples.Example).Count | Should -BeGreaterOrEqual 2
            $help.Parameters.Parameter.Name | Should -Contain 'Path'
            $help.Parameters.Parameter.Name | Should -Contain 'Name'
            (Get-Command $command).Parameters.Keys | Should -Contain 'WhatIf'
        }
    }

    It 'Ships no copied supplemental theme or grammar assets' {
        $library = Join-Path $PSScriptRoot '..' 'output' 'lib'
        Join-Path $library 'grammars' | Should -Not -Exist
        Join-Path $library 'themes' | Should -Not -Exist
    }
}
