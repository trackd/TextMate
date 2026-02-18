BeforeAll {
    if (-not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }
}


Describe 'Get-TextMateGrammar' {
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
