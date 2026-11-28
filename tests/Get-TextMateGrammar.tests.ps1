BeforeAll {
    if (-not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }
}


Describe 'Get-TextMateGrammar' {
    It 'Discovers and highlights bundled KDL v1 files without registration' {
        $grammar = Get-TextMateGrammar | Where-Object Id -EQ 'kdl'
        $grammar.ScopeName | Should -Be 'source.kdl'
        $grammar.Aliases | Should -Contain 'KDL v1'
        $grammar.Extensions | Should -Contain '.kdl'
        $file = Join-Path $TestDrive 'config.kdl'
        $source = 'node enabled=true title="sample"'
        Set-Content -LiteralPath $file -Value $source -NoNewline
        $rendered = Format-TextMate $file -OutputFormat Text -ErrorAction Stop
        $document = [PSTextMate.Core.OutputRenderer]::GetDocument($rendered)
        $document.Language | Should -Be 'kdl'
        $document.Source | Should -BeExactly $source
        Join-Path $PSScriptRoot '..' 'output' 'lib' 'LICENSE.KDL.txt' | Should -Exist
    }
    It 'Returns at least one available language' {
        $result = Get-TextMateGrammar
        $result | Should -Not -BeNullOrEmpty
    }
    It 'Should have Help and examples' {
        $help = Get-Help Get-TextMateGrammar -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
        $help.examples.example.Count | Should -BeGreaterThan 1
    }
}
